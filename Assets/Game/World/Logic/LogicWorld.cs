using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Game.Combat;
using Game.Scripts.Fixed;
using Game.Unit;
using Game.Unit.Ability.BaseAbilities;

namespace Game.World.Logic
{
    /// <summary>
    /// Pure simulation world. Runs on a dedicated background thread.
    /// Never call UnityEngine APIs from this class.
    /// </summary>
    public sealed class LogicWorld : IOccupancyView
    {
        public readonly struct AbilityTickRates
        {
            // 0 means every tick; otherwise, tick when (worldTick % rate == 0).
            public readonly int Movement;
            public readonly int Guard;
            public readonly int Weapon;
            public readonly int Navigation;

            public AbilityTickRates(int movement = 0, int guard = 0, int weapon = 0, int navigation = 0)
            {
                Movement = movement;
                Guard = guard;
                Weapon = weapon;
                Navigation = navigation;
            }

            public static AbilityTickRates Default => new AbilityTickRates();
        }

        public readonly struct ActivityTickRates
        {
            // 0 means every tick; otherwise, tick when (worldTick % rate == 0).
            public readonly int GuardActivity;
            public readonly int ChaseTarget;
            public readonly int Navigate;
            public readonly int Move;
            public readonly int Idle;

            public ActivityTickRates(int guardActivity = 0, int chaseTarget = 0, int navigate = 0, int move = 0, int idle = 0)
            {
                GuardActivity = guardActivity;
                ChaseTarget = chaseTarget;
                Navigate = navigate;
                Move = move;
                Idle = idle;
            }

            public static ActivityTickRates Default => new ActivityTickRates();
        }

        private readonly LayeredOccupancyIndex _occupancy = new LayeredOccupancyIndex();
        public LayeredOccupancyIndex Occupancy => _occupancy;
        public readonly int TickRate;

        private readonly EnemySearchService _enemySearch;
        public IEnemySearchService EnemySearch => _enemySearch;

        private readonly ConcurrentQueue<ILogicInput> _in;
        private readonly ConcurrentQueue<ILogicOutput> _out;
        private readonly List<Actor> _actors = new List<Actor>(256);

        // Commands scheduled for execution at the beginning of the next TickOnce().
        // LogicWorld doesn't care which render frame/network frame they came from;
        // it executes exactly what World provides, in FIFO order.
        private readonly Queue<ILogicCommand> _pendingCommands = new Queue<ILogicCommand>(64);

        // NEW: deterministic scheduled command buffer keyed by tick -> sorted by sequence.
        private readonly SortedDictionary<int, List<ScheduledCommand>> _scheduled = new SortedDictionary<int, List<ScheduledCommand>>();
        private readonly struct ScheduledCommand
        {
            public readonly int Sequence;
            public readonly ILogicCommand Command;
            public ScheduledCommand(int sequence, ILogicCommand command) { Sequence = sequence; Command = command; }
        }

        private int _tick;
        public int Tick => _tick;
        private readonly Game.Map.IMap _map;

        public Game.Map.IMap Map => _map;

        private readonly IArchetypeRegistry _archetypes;

        private readonly AbilityTickRates _abilityTickRates;
        private readonly ActivityTickRates _activityTickRates;

        // ── Combat ───────────────────────────────────────────────────────
        private CombatRegistry _combatRegistry;
        /// <summary>Combat definitions (weapons, warheads, projectiles). Set after construction.</summary>
        public CombatRegistry CombatData => _combatRegistry ?? CombatRegistry.Empty;

        // Active projectiles — ticked each logic tick, removed when finished.
        private readonly List<ProjectileActor> _projectiles = new List<ProjectileActor>(64);
        // Pending additions (avoids modifying list while iterating).
        private readonly List<ProjectileActor> _projectilesPending = new List<ProjectileActor>(16);

        /// <summary>Inject combat data. Must be called before Run() or during setup.</summary>
        public void SetCombatRegistry(CombatRegistry registry) { _combatRegistry = registry; }

        // Reusable list for spatial queries (avoids per-frame allocation).
        private readonly List<Actor> _spatialQueryResult = new List<Actor>(64);

        // Reusable snapshot array (avoids per-tick allocation). Resized if actor count changes.
        private RenderUnitSnapshot[] _snapshotBuffer = new RenderUnitSnapshot[256];

        public LogicWorld(int tickRate, ConcurrentQueue<ILogicInput> input, ConcurrentQueue<ILogicOutput> output, Game.Map.IMap map = null, int enemySearchPartitionCellSize = 5, AbilityTickRates? abilityTickRates = null, ActivityTickRates? activityTickRates = null)
        {
            if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate));
            TickRate = tickRate;
            _in = input ?? throw new ArgumentNullException(nameof(input));
            _out = output ?? throw new ArgumentNullException(nameof(output));
            _map = map;

            _abilityTickRates = abilityTickRates ?? AbilityTickRates.Default;
            _activityTickRates = activityTickRates ?? ActivityTickRates.Default;

            // Enable incremental occupancy updates.
            _occupancy.SetMap(_map);

            // Construct enemy search service (partitioned index). Partition cell size min = 5.
            _enemySearch = new EnemySearchService(this, _map, enemySearchPartitionCellSize, new DefaultTargetingRules());
        }

        internal LogicWorld(int tickRate,
            ConcurrentQueue<ILogicInput> input,
            ConcurrentQueue<ILogicOutput> output,
            Game.Map.IMap map,
            int enemySearchPartitionCellSize,
            IArchetypeRegistry archetypes)
            : this(tickRate, input, output, map, enemySearchPartitionCellSize, abilityTickRates: null, activityTickRates: null)
        {
            _archetypes = archetypes;
        }

        internal LogicWorld(int tickRate,
            ConcurrentQueue<ILogicInput> input,
            ConcurrentQueue<ILogicOutput> output,
            Game.Map.IMap map,
            int enemySearchPartitionCellSize,
            IArchetypeRegistry archetypes,
            AbilityTickRates abilityTickRates,
            ActivityTickRates activityTickRates)
            : this(tickRate, input, output, map, enemySearchPartitionCellSize, abilityTickRates, activityTickRates)
        {
            _archetypes = archetypes;
        }

        internal bool TryGetArchetype(int archetypeId, out Game.Serialization.UnitData data)
        {
            data = null;
            if (archetypeId <= 0) return false;
            return _archetypes != null && _archetypes.TryGetUnitData(archetypeId, out data) && data != null;
        }

        internal bool TryGetArchetype(string archetypeStringId, out Game.Serialization.UnitData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(archetypeStringId)) return false;
            return _archetypes != null && _archetypes.TryGetUnitData(archetypeStringId, out data) && data != null;
        }

        private readonly Dictionary<int, Actor> _actorById = new Dictionary<int, Actor>(512);

        public bool TryGetActorById(int actorId, out Actor actor)
        {
            actor = null;
            if (actorId == 0) return false;
            return _actorById.TryGetValue(actorId, out actor) && actor != null;
        }

        public void AddActor(Actor actor)
        {
            if (actor == null) return;

            // Expose read-only world services to the actor (logic thread only).
            actor.World = this;

            // Ensure activity stack exists so commands like Movement.MoveTo can push activities safely.
            if (actor.Activities == null)
                actor.Activities = new Stack<Game.Unit.Activity.IActivity>();
            if (actor.Activities.Count == 0)
                actor.Activities.Push(new Game.Unit.Activity.IdleActivity());

            _actors.Add(actor);

            // Maintain id index (deterministic). Actor.Id is expected to be stable for root units.
            if (actor.Id != 0)
                _actorById[actor.Id] = actor;

            // Prime occupancy for newly added actors so queries are correct immediately.
            try { _occupancy.UpdateActor(actor); }
            catch { /* don't throw in logic thread */ }

            // Prime partition index too.
            try { _enemySearch?.UpdateActorPartition(actor); }
            catch { /* don't throw in logic thread */ }
        }

        /// <summary>Remove an actor from the world (deferred-safe). Uses swap-remove for O(1).</summary>
        public void RemoveActor(Actor actor)
        {
            if (actor == null) return;
            if (actor.Id != 0) _actorById.Remove(actor.Id);
            int idx = _actors.IndexOf(actor);
            if (idx < 0) return;
            int last = _actors.Count - 1;
            if (idx != last)
                _actors[idx] = _actors[last];
            _actors.RemoveAt(last);
        }

        // ── Combat helpers (called from Armament / ProjectileActor) ─────

        /// <summary>
        /// Spawn a projectile into the logic world. Called from Armament.ExecuteFire().
        /// </summary>
        internal void SpawnProjectile(WeaponDef weaponDef, ProjectileDef projDef, string warheadId,
            Actor source, Actor target, FixedVector3 startPos, FixedVector3 targetPos)
        {
            if (projDef == null) return;
            var proj = new ProjectileActor(projDef, weaponDef, warheadId, source, target, startPos, targetPos);
            _projectilesPending.Add(proj);
        }

        /// <summary>
        /// Find all actors within radius of a point. Returns a reusable list (do not cache!).
        /// Uses spatial partition index when available for O(k) instead of O(n).
        /// </summary>
        public List<Actor> FindActorsInRadius(FixedVector3 center, int radiusLogicUnits)
        {
            _spatialQueryResult.Clear();

            var radiusFixed = Fixed.FromRaw(radiusLogicUnits);
            var rSqFixed = radiusFixed * radiusFixed;

            // Brute-force scan — all actors. Partition-accelerated scan is left as
            // a future optimization (EnemySearchService partitions are per-faction,
            // but FindActorsInRadius needs ALL factions for splash damage).
            for (int i = 0; i < _actors.Count; i++)
            {
                var a = _actors[i];
                if (a == null) continue;

                // Hot path: use indexed for-loop instead of foreach to avoid enumerator allocation
                Location loc = null;
                var abs = a.Abilities;
                for (int j = 0; j < abs.Count; j++)
                {
                    if (abs[j] is Location l) { loc = l; break; }
                }
                if (loc == null) continue;

                var diff = center - loc.Position;
                var distSq = diff.SqrMagnitude();
                if (distSq.Raw <= rSqFixed.Raw)
                    _spatialQueryResult.Add(a);
            }

            return _spatialQueryResult;
        }

        private void TickProjectiles()
        {
            // Merge pending projectiles
            if (_projectilesPending.Count > 0)
            {
                for (int i = 0; i < _projectilesPending.Count; i++)
                    _projectiles.Add(_projectilesPending[i]);
                _projectilesPending.Clear();
            }

            // Tick all, then compact finished entries with swap-and-truncate (O(n), not O(n²)).
            for (int i = 0; i < _projectiles.Count; i++)
            {
                var p = _projectiles[i];
                try { p.Tick(this); }
                catch { p.Finished = true; }
            }

            // Remove finished: swap last alive into each hole, shrink count.
            int writeIdx = 0;
            for (int i = 0; i < _projectiles.Count; i++)
            {
                if (!_projectiles[i].Finished)
                {
                    if (writeIdx != i)
                        _projectiles[writeIdx] = _projectiles[i];
                    writeIdx++;
                }
            }
            if (writeIdx < _projectiles.Count)
                _projectiles.RemoveRange(writeIdx, _projectiles.Count - writeIdx);
        }

        private void CleanupDeadActors()
        {
            // Compact dead/null actors with single-pass swap (O(n), not O(n²)).
            int writeIdx = 0;
            for (int i = 0; i < _actors.Count; i++)
            {
                var a = _actors[i];
                if (a == null) continue;

                // Check if Health says dead
                bool dead = false;
                for (int j = 0; j < a.Abilities.Count; j++)
                {
                    if (a.Abilities[j] is Health h && h.IsDead)
                    {
                        dead = true;
                        break;
                    }
                }

                if (dead)
                {
                    if (a.Id != 0) _actorById.Remove(a.Id);
                    // Future: enqueue death event for rendering (explosion VFX, etc.)
                    continue; // skip — don't copy to writeIdx
                }

                if (writeIdx != i)
                    _actors[writeIdx] = a;
                writeIdx++;
            }
            if (writeIdx < _actors.Count)
                _actors.RemoveRange(writeIdx, _actors.Count - writeIdx);
        }

        public void Run(CancellationToken token)
        {
            // fixed timestep loop
            var tickMs = 1000 / TickRate;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long next = sw.ElapsedMilliseconds;

            while (!token.IsCancellationRequested)
            {
                var now = sw.ElapsedMilliseconds;
                if (now < next)
                {
                    // sleep a bit to reduce CPU; keep short to reduce jitter
                    Thread.Sleep(0);
                    continue;
                }

                next += tickMs;
                TickOnce();

                // If we lag behind, resync (avoid spiral of death)
                if (sw.ElapsedMilliseconds - next > tickMs * 4)
                    next = sw.ElapsedMilliseconds;
            }
        }

        public void EnqueueCommand(ILogicCommand cmd)
        {
            if (cmd == null) return;
            _pendingCommands.Enqueue(cmd);
        }

        /// <summary>
        /// Enqueue a command intended for a specific logic tick, with a deterministic sequence tie-break.
        /// tick less-or-equal 0 keeps legacy behavior (execute ASAP).
        /// </summary>
        public void EnqueueCommandAt(ILogicCommand cmd, int tick, int sequence)
        {
            if (cmd == null) return;
            if (tick <= 0)
            {
                _pendingCommands.Enqueue(cmd);
                return;
            }

            if (!_scheduled.TryGetValue(tick, out var list) || list == null)
            {
                list = new List<ScheduledCommand>(4);
                _scheduled[tick] = list;
            }

            list.Add(new ScheduledCommand(sequence, cmd));
        }

        private bool ShouldTickAbility(IAbility ab)
        {
            if (ab == null) return false;
            int rate;
            // Map ability type -> configured rate.
            if (ab is Game.Unit.Ability.BaseAbilities.Movement) rate = _abilityTickRates.Movement;
            else if (ab is Game.Unit.Ability.BaseAbilities.Guard) rate = _abilityTickRates.Guard;
            else if (ab is Game.Unit.Ability.BaseAbilities.Weapon) rate = _abilityTickRates.Weapon;
            else if (ab is Game.Unit.Ability.Navigation) rate = _abilityTickRates.Navigation;
            else rate = 0; // default: tick every tick

            if (rate <= 0) return true;
            // Deterministic: only depends on world tick.
            return (_tick % rate) == 0;
        }

        // NOTE: Activity tick-rate throttling is currently disabled.
        // Reason: ChaseTarget must observe target movement every tick to cancel/replan Navigate;
        // throttling Actor.Tick() would break continuous chase and cause units to keep walking
        // toward stale target positions.
        // We keep ActivityTickRates injected/configured for future use when we add a safe
        // per-activity tick scheduler that doesn't block replanning.
        private static bool ShouldTickTopActivity(Actor a) => a != null && a.Activities != null && a.Activities.Count > 0;

        public void TickOnce()
        {
            // 1) Drain inputs (inputs may enqueue commands).
            while (_in.TryDequeue(out var msg))
            {
                try { msg.Apply(this); }
                catch (Exception e) { _out.Enqueue(new LogicError(e)); }
            }

            // 2) Execute all commands scheduled for this tick.
            // 2.1) Deterministic scheduled commands for the current tick (sorted by sequence).
            if (_scheduled.TryGetValue(_tick, out var scheduledNow) && scheduledNow != null && scheduledNow.Count > 0)
            {
                scheduledNow.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
                for (int i = 0; i < scheduledNow.Count; i++)
                {
                    try { scheduledNow[i].Command.Execute(this); }
                    catch (Exception e) { _out.Enqueue(new LogicError(e)); }
                }
                _scheduled.Remove(_tick);
            }

            // 2.2) Legacy ASAP commands (FIFO).
            while (_pendingCommands.Count > 0)
            {
                var cmd = _pendingCommands.Dequeue();
                try { cmd.Execute(this); }
                catch (Exception e) { _out.Enqueue(new LogicError(e)); }
            }

            // 3) Tick actors, and update occupancy incrementally after each actor ticks.
            for (int i = 0; i < _actors.Count; i++)
            {
                var a = _actors[i];
                if (a == null) continue;

                // Tick abilities first (movement parameters, weapons, etc.), then the current top activity.
                // Abilities are expected to be side-effect free with respect to Unity APIs (logic thread).
                var abs = a.Abilities;
                for (int ai = 0; ai < abs.Count; ai++)
                {
                    var ab = abs[ai];
                    if (!ShouldTickAbility(ab)) continue;
                    try { ab?.Tick(); }
                    catch { /* don't throw in logic thread */ }
                }

                // Activities must tick every logic tick for correct responsiveness.
                a.Tick();

                try
                {
                    _occupancy.UpdateActor(a);
                }
                catch { /* don't throw in logic thread */ }

                // Update spatial partition after movement/position changes.
                // This is the required hook: "unit each move完后更新自己的分区".
                try
                {
                    _enemySearch?.UpdateActorPartition(a);
                }
                catch { /* don't throw in logic thread */ }
            }

            // 3b) Tick projectiles (separate from actors — projectiles are lightweight).
            TickProjectiles();

            // 3c) Remove dead actors (deferred to avoid modifying list during iteration).
            CleanupDeadActors();

            _tick++;

            // 4) Publish snapshots.
            // RenderSnapshot: lightweight, high-frequency — always needed.
            _out.Enqueue(BuildRenderSnapshot());
            // LogicSnapshot: heavier debug snapshot — only when main thread has logging enabled.
            if (WorldDebugAccess.ShouldBuildLogicSnapshot)
                _out.Enqueue(BuildSnapshot());
        }

        private RenderSnapshot BuildRenderSnapshot()
        {
            // Reuse buffer; only re-allocate if actor count grew.
            int count = _actors.Count;
            if (_snapshotBuffer.Length < count)
                _snapshotBuffer = new RenderUnitSnapshot[count + count / 4]; // grow with 25% headroom

            for (int i = 0; i < count; i++)
            {
                var a = _actors[i];
                FixedVector3 pos = FixedVector3.Zero;
                FixedQuaternion rot = FixedQuaternion.Identity;

                int factionId = 0;
                int playerId = -1;

                string topActivity = null;
                string[] activityStack = null;
                string[] abilities = null;

                int currentHp = 0;
                int maxHp = 0;

                if (a != null)
                {
                    factionId = a.Faction;
                    playerId = a.OwnerPlayerId;

                    // Debug flags (read from World on main thread).
                    var dbg = WorldDebugAccess.GetRenderUnitDebugFlags();

                    if (dbg.SyncTopActivity)
                    {
                        if (a.Activities != null && a.Activities.Count > 0 && a.Activities.Peek() != null)
                            topActivity = a.Activities.Peek().GetType().Name;
                    }

                    if (dbg.SyncActivities)
                    {
                        // Full stack snapshot (top -> bottom).
                        if (a.Activities != null && a.Activities.Count > 0)
                        {
                            activityStack = new string[a.Activities.Count];
                            int si = 0;
                            foreach (var act in a.Activities)
                                activityStack[si++] = act != null ? act.GetType().Name : "<null>";
                        }
                    }

                    if (dbg.SyncAbilities)
                    {
                        if (a.Abilities != null && a.Abilities.Count > 0)
                        {
                            var tmp = new List<string>(a.Abilities.Count);
                            foreach (var ab in a.Abilities)
                                tmp.Add(ab != null ? ab.GetType().Name : "<null>");

                            // Deterministic order (HashSet iteration order is undefined).
                            tmp.Sort(StringComparer.Ordinal);
                            abilities = tmp.ToArray();
                        }

                        // Child debug: count + child abilities.
                        var ch = a.Children;
                        if (ch != null)
                        {
                            // store into locals declared outside in future; for now, create inline variables
                        }
                    }

                    foreach (var ab in a.Abilities)
                    {
                        if (ab is Location l)
                        {
                            pos = l.Position;
                            rot = l.Rotation;
                        }
                        else if (ab is Health h)
                        {
                            currentHp = h.HP;
                            maxHp = h.MaxHP;
                        }

                        // We can break once we've found both Location and Health.
                        if (maxHp != 0 && !(pos.Equals(FixedVector3.Zero) && rot.Equals(FixedQuaternion.Identity)))
                        {
                            // Note: this heuristic isn't perfect if a unit is actually at origin, but it's fine.
                            // We'll just keep iterating in that case.
                        }
                    }
                }

                var id = (a != null && a.Id != 0) ? a.Id : (i + 1);
                // Keep legacy ctor for positional data, but fill ownership metadata too.
                var color = Game.Unit.PlayerPalette.GetColor(Game.Unit.PlayerPalette.ClampPlayerId(playerId));
                var cFixed = new FixedVector3(Fixed.FromFloat(color.r), Fixed.FromFloat(color.g), Fixed.FromFloat(color.b));
                int? childCount = null;
                string[][] childAbilities = null;
                string rootArchId = null;
                try
                {
                    var dbg2 = WorldDebugAccess.GetRenderUnitDebugFlags();
                    if (dbg2.SyncAbilities && a != null)
                    {
                        rootArchId = a.DebugArchetypeId;
                        var ch = a.Children;
                        if (ch != null)
                        {
                            childCount = ch.Count;
                            childAbilities = new string[ch.Count][];
                            for (int ci = 0; ci < ch.Count; ci++)
                            {
                                var ca = ch[ci];
                                if (ca == null || ca.Abilities == null || ca.Abilities.Count == 0)
                                {
                                    childAbilities[ci] = Array.Empty<string>();
                                    continue;
                                }

                                var tmp2 = new List<string>(ca.Abilities.Count);
                                foreach (var ab in ca.Abilities)
                                    tmp2.Add(ab != null ? ab.GetType().Name : "<null>");
                                tmp2.Sort(StringComparer.Ordinal);
                                childAbilities[ci] = tmp2.ToArray();
                            }
                        }
                    }
                }
                catch { /* debug-only */ }

                _snapshotBuffer[i] = new RenderUnitSnapshot(id, i, pos, rot, factionId, playerId, cFixed, currentHp, maxHp, topActivity, activityStack, abilities, childCount, childAbilities, rootArchId);
            }

            // Copy active region to a right-sized array for the snapshot.
            // This allocation is unavoidable (snapshot crosses thread boundary), but the
            // per-unit construction above avoids re-building the full buffer.
            var result = new RenderUnitSnapshot[count];
            Array.Copy(_snapshotBuffer, result, count);
            return new RenderSnapshot(_tick, result);
        }

        private LogicSnapshot BuildSnapshot()
        {
            // Flatten actor graph, then keep only primary gameplay units.
            var allActors = new List<Actor>(_actors.Count * 2);
            for (int i = 0; i < _actors.Count; i++)
                CollectActorRecursive(_actors[i], allActors);

            // Filter: Units.
            // Primary rule: Actor.IsPrimaryUnit (from UnitData.IsPrimary).
            // Fallback rule: any actor that has a Location ability is considered a unit (gameplay presence).
            var unitActors = new List<Actor>(_actors.Count);
            for (int i = 0; i < allActors.Count; i++)
            {
                var a = allActors[i];
                if (a == null) continue;

                if (a.IsPrimaryUnit)
                {
                    unitActors.Add(a);
                    continue;
                }

                // Fallback: has location => treat as unit
                foreach (var ab in a.Abilities)
                {
                    if (ab is Location)
                    {
                        unitActors.Add(a);
                        break;
                    }
                }
            }

            var units = new LogicUnitSnapshot[unitActors.Count];
            for (int i = 0; i < unitActors.Count; i++)
            {
                var a = unitActors[i];

                int? hp = null;
                FixedVector3? pos = null;

                foreach (var ab in a.Abilities)
                {
                    if (hp == null && ab is Health h)
                        hp = h.HP;
                    else if (pos == null && ab is Location l)
                        pos = l.Position;

                    if (hp != null && pos != null)
                        break;
                }

                units[i] = new LogicUnitSnapshot(
                    index: i,
                    name: a.ToString(),
                    hp: hp,
                    position: pos
                );
            }

            return new LogicSnapshot(_tick, units);
        }

        private static void CollectActorRecursive(Actor root, List<Actor> dst)
        {
            if (root == null || dst == null) return;
            dst.Add(root);

            var children = root.Children;
            if (children == null) return;
            for (int i = 0; i < children.Count; i++)
            {
                CollectActorRecursive(children[i], dst);
            }
        }

        /// <summary>
        /// Try get a root actor by index (0-based).
        /// This is intended for test scaffolding / command dispatch glue.
        /// </summary>
        public bool TryGetRootActorByIndex(int index, out Actor actor)
        {
            actor = null;
            if (index < 0 || index >= _actors.Count) return false;
            actor = _actors[index];
            return actor != null;
        }
    }

    public interface ILogicInput
    {
        void Apply(LogicWorld world);
    }

    public interface ILogicOutput { }

    public readonly struct LogicTicked : ILogicOutput
    {
        public readonly int Tick;
        public LogicTicked(int tick) { Tick = tick; }
    }

    public readonly struct LogicError : ILogicOutput
    {
        public readonly Exception Exception;
        public LogicError(Exception exception) { Exception = exception; }
    }

    /// <summary>
    /// Executable logic command.
    /// LogicWorld executes all commands at the beginning of each tick.
    /// </summary>
    public interface ILogicCommand
    {
        void Execute(LogicWorld world);
    }

    /// <summary>
    /// Snapshot of the entire logic world for a tick.
    /// Intended for rendering/debug only (main thread consumption).
    /// </summary>
    public readonly struct LogicSnapshot : ILogicOutput
    {
        public readonly int Tick;
        public readonly LogicUnitSnapshot[] Units;

        public LogicSnapshot(int tick, LogicUnitSnapshot[] units)
        {
            Tick = tick;
            Units = units;
        }
    }

    public readonly struct LogicUnitSnapshot
    {
        public readonly int Index;
        public readonly string Name;
        public readonly int? Hp;
        public readonly FixedVector3? Position;

        public LogicUnitSnapshot(int index, string name, int? hp, FixedVector3? position)
        {
            Index = index;
            Name = name;
            Hp = hp;
            Position = position;
        }

        public override string ToString()
        {
            return $"[{Index}] {Name} hp={(Hp.HasValue ? Hp.Value.ToString() : "-")} pos={(Position.HasValue ? Position.Value.ToString() : "-")}";
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  ProjectileActor — lightweight logic-only projectile
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runtime projectile in the logic world. Not a full Actor — kept in a separate list
    /// for performance (projectiles don't need occupancy, enemy search, activity stacks).
    /// Reference: OpenRA Projectile / RA2 BulletType.
    /// </summary>
    internal sealed class ProjectileActor
    {
        public readonly ProjectileDef Def;
        public readonly WeaponDef WeaponDef;
        public readonly string WarheadId;
        public readonly Actor Source;
        public readonly Actor Target;       // may be null for ground-target
        public readonly FixedVector3 TargetPos;

        public FixedVector3 Position;
        public FixedVector3 Velocity;
        public int Lifetime;
        public int PierceCount;
        public bool Finished;

        // Cached for ballistic arc calculation
        public readonly FixedVector3 StartPos;

        public ProjectileActor(ProjectileDef def, WeaponDef weaponDef, string warheadId,
            Actor source, Actor target, FixedVector3 startPos, FixedVector3 targetPos)
        {
            Def = def;
            WeaponDef = weaponDef;
            WarheadId = warheadId;
            Source = source;
            Target = target;
            TargetPos = targetPos;
            Position = startPos;
            StartPos = startPos;
            Velocity = FixedVector3.Zero;
        }

        public void Tick(LogicWorld world)
        {
            if (Finished) return;
            Lifetime++;
            if (Lifetime > Def.MaxLifetime) { Finished = true; return; }

            switch (Def.Type)
            {
                case ProjectileType.Bullet:  TickBullet(); break;
                case ProjectileType.Missile: TickMissile(); break;
                case ProjectileType.Ballistic: TickBallistic(); break;
                case ProjectileType.Torpedo: TickMissile(); break; // same tracking, render constrains Y
                case ProjectileType.Beam:    TickBeam(world); return;
            }

            CheckHit(world);
        }

        private void TickBullet()
        {
            var diff = TargetPos - Position;
            if (diff.SqrMagnitude().Raw == 0) { Finished = true; return; }
            var dir = diff.Normalized();
            var speed = Fixed.FromRaw(Def.Speed << 10); // scale to Fixed
            Velocity = dir * speed;
            Position = Position + Velocity;
        }

        private void TickMissile()
        {
            var actualTarget = (Target != null && !IsTargetDead())
                ? GetTargetPosition() : TargetPos;
            var diff = actualTarget - Position;
            if (diff.SqrMagnitude().Raw == 0) { Finished = true; return; }
            var desired = diff.Normalized();
            var speed = Fixed.FromRaw(Def.Speed << 10);

            if (Def.TurnRate > 0 && Velocity.SqrMagnitude().Raw > 0)
            {
                // Limited turning — interpolate direction
                var curDir = Velocity.Normalized();
                var turnFrac = Fixed.FromRatio(Def.TurnRate, 1024);
                var newDir = new FixedVector3(
                    curDir.x + (desired.x - curDir.x) * turnFrac,
                    curDir.y + (desired.y - curDir.y) * turnFrac,
                    curDir.z + (desired.z - curDir.z) * turnFrac
                ).Normalized();
                Velocity = newDir * speed;
            }
            else
            {
                Velocity = desired * speed;
            }

            Position = Position + Velocity;
        }

        private void TickBallistic()
        {
            // Use StartPos → TargetPos for total distance (computed once is fine since these are readonly)
            var totalDiff = TargetPos - StartPos;
            var totalDist = totalDiff.Magnitude();
            var speed = Fixed.FromRaw(Def.Speed << 10);
            if (totalDist.Raw == 0 || speed.Raw == 0) { Finished = true; return; }

            int totalTicks = totalDist.Raw / speed.Raw;
            if (totalTicks < 1) totalTicks = 1;

            if (Lifetime >= totalTicks)
            {
                Position = TargetPos;
                return; // will be caught by CheckHit
            }

            // Linear interpolation from start to target
            var tNorm = Fixed.FromRatio(Lifetime, totalTicks);
            var basePos = new FixedVector3(
                StartPos.x + totalDiff.x * tNorm,
                StartPos.y + totalDiff.y * tNorm,
                StartPos.z + totalDiff.z * tNorm);

            // Arc height: parabola peak at midpoint — 4*h*t*(1-t)
            var arcH = Fixed.FromRaw(Def.ArcHeight << 10);
            var arc = arcH * Fixed.FromInt(4) * tNorm * (Fixed.One - tNorm);
            Position = new FixedVector3(basePos.x, basePos.y + arc, basePos.z);
        }

        private void TickBeam(LogicWorld world)
        {
            // Beam: instant damage on first tick, persists visually
            if (Lifetime == 1)
                OnHit(world);
            if (Lifetime >= 3) // visual duration
                Finished = true;
        }

        private void CheckHit(LogicWorld world)
        {
            var hitR = Fixed.FromRaw(Def.HitRadius);
            var hitRSq = hitR * hitR;

            // Check target actor
            if (Target != null && !IsTargetDead())
            {
                var tPos = GetTargetPosition();
                var dSq = (Position - tPos).SqrMagnitude();
                if (dSq.Raw <= hitRSq.Raw)
                {
                    OnHit(world);
                    return;
                }
            }

            // Check target position (ground target / target lost)
            var dPosSq = (Position - TargetPos).SqrMagnitude();
            if (dPosSq.Raw <= hitRSq.Raw)
            {
                OnHit(world);
            }
        }

        private void OnHit(LogicWorld world)
        {
            var registry = world.CombatData;
            var warhead = registry?.GetWarhead(WarheadId);
            if (warhead == null) { Finished = true; return; }

            if (warhead.SplashRadius > 0)
            {
                // AOE at impact position
                var victims = world.FindActorsInRadius(Position, warhead.SplashRadius);
                if (victims != null)
                {
                    for (int i = 0; i < victims.Count; i++)
                    {
                        var victim = victims[i];
                        if (victim == null) continue;
                        if (!warhead.AffectsAllies && victim.Faction == Source?.Faction && victim != Source) continue;
                        if (!warhead.AffectsSelf && victim == Source) continue;

                        var victimLoc = FindLocation(victim);
                        if (victimLoc == null) continue;

                        int dist = (Position - victimLoc.Position).Magnitude().Raw;
                        int falloff = warhead.GetFalloff(dist >> Fixed.SHIFT); // convert Fixed raw to integer world units
                        if (falloff <= 0) continue;

                        var armorType = GetArmorType(victim);
                        var packet = DamageResolver.BuildPacket(warhead, WeaponDef, armorType, victim.UnitAlertLayer, Source, falloff);
                        var health = FindHealth(victim);
                        health?.InflictDamage(packet);

                        if (warhead.DotDamagePerTick > 0 && warhead.DotDuration > 0)
                            health?.ApplyDoT(warhead.DotDamagePerTick, warhead.DotDuration, warhead.DotDamageType, Source);
                    }
                }
            }
            else if (Target != null)
            {
                // Single target
                var health = FindHealth(Target);
                if (health != null)
                {
                    var armorType = GetArmorType(Target);
                    var packet = DamageResolver.BuildPacket(warhead, WeaponDef, armorType, Target.UnitAlertLayer, Source);
                    health.InflictDamage(packet);

                    if (warhead.DotDamagePerTick > 0 && warhead.DotDuration > 0)
                        health.ApplyDoT(warhead.DotDamagePerTick, warhead.DotDuration, warhead.DotDamageType, Source);
                }
            }

            if (Def.Piercing && PierceCount < Def.MaxPierceCount)
            {
                PierceCount++;
                // Continue flying
            }
            else
            {
                Finished = true;
            }
        }

        private bool IsTargetDead()
        {
            var h = FindHealth(Target);
            return h != null && h.IsDead;
        }

        private FixedVector3 GetTargetPosition()
        {
            var loc = FindLocation(Target);
            return loc != null ? loc.Position : TargetPos;
        }

        private static Health FindHealth(Actor a)
        {
            if (a?.Abilities == null) return null;
            foreach (var ab in a.Abilities) if (ab is Health h) return h;
            return null;
        }

        private static Location FindLocation(Actor a)
        {
            if (a?.Abilities == null) return null;
            foreach (var ab in a.Abilities) if (ab is Location l) return l;
            return null;
        }

        private static ArmorType GetArmorType(Actor a)
        {
            if (a?.Abilities == null) return ArmorType.None;
            foreach (var ab in a.Abilities) if (ab is Game.Unit.Ability.ArmorInfo ai) return ai.Armor;
            return ArmorType.None;
        }
    }
}


