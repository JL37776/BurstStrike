using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Very simple render proxy for a logic unit.
    /// It only tracks the logic unit snapshot Index and updates its Transform from snapshot Position.
    /// </summary>
    public sealed class RenderUnit : MonoBehaviour
    {
        public int UnitId { get; private set; }

        [Header("Ownership (Debug)")]
        [SerializeField] private int ownerUserId;
        [SerializeField] private int factionId;
        [SerializeField] private Color unitColor = Color.white;

        [Header("Activity (Debug)")]
        [SerializeField] private string topActivity;
        [SerializeField] private string[] activityStack;

        [Header("Abilities (Debug)")]
        [SerializeField] private string[] abilities;

        [Header("Partition (Debug)")]
        [SerializeField] private int partitionX;
        [SerializeField] private int partitionY;
        [SerializeField] private int partitionCellSize;

        [Header("Children (Debug)")]
        [SerializeField] private int childCount;
        [SerializeField] private string[][] childAbilities;

        [Header("Archetype (Debug)")]
        [SerializeField] private string rootArchetypeId;

        [Header("Health (Debug)")]
        [SerializeField] private int currentHp;
        [SerializeField] private int maxHp;

        public int OwnerUserId => ownerUserId;
        public int FactionId => factionId;
        public Color UnitColor => unitColor;
        public string TopActivity => topActivity;
        public string[] ActivityStack => activityStack;
        public string[] Abilities => abilities;
        // Unit's current partition (based on its position).
        public int PartitionX => partitionX;
        public int PartitionY => partitionY;
        public int PartitionCellSize => partitionCellSize;

        // Back-compat: old names used in earlier debug UI.
        public int EnemyPartitionX => partitionX;
        public int EnemyPartitionY => partitionY;
        public int EnemyPartitionCellSize => partitionCellSize;

        private Transform _forwardIndicator;

        [Header("Trail")]
        public bool enableTrail = true;
        public int trailMaxPoints = 64;
        public float trailMinDistance = 0.05f;
        public float trailWidth = 0.06f;
        public Color trailColor = new Color(1f, 1f, 1f, 0.6f);

        private LineRenderer _trail;
        private readonly System.Collections.Generic.List<Vector3> _trailPoints = new System.Collections.Generic.List<Vector3>(128);
        private Renderer _renderer;

        public void ApplyOwnership(int newOwnerUserId, int newFactionId, Color color)
        {
            ownerUserId = newOwnerUserId;
            factionId = newFactionId;
            unitColor = color;

            _renderer ??= GetComponent<Renderer>();
            if (_renderer != null)
            {
                // IMPORTANT: don't mutate sharedMaterial (it may be shared across instances and cause confusing mismatches).
                // Instead, ensure we have a unique instance per unit.
                var mat = _renderer.material;
                if (mat == null) mat = new Material(Shader.Find("Standard"));
                mat.color = unitColor;
                _renderer.material = mat;
            }

            // Keep forward indicator color in sync as well.
            if (_forwardIndicator != null)
            {
                var r = _forwardIndicator.GetComponent<Renderer>();
                if (r != null)
                {
                    var m = r.material;
                    if (m == null) m = new Material(Shader.Find("Standard"));
                    m.color = unitColor;
                    r.material = m;
                }
            }

            if (_trail != null)
            {
                // Keep alpha from trailColor but use unit hue.
                var c = unitColor;
                c.a = trailColor.a;
                _trail.material.color = c;
            }
        }

        // Back-compat property
        public int SnapshotIndex => UnitId;

        public void ApplyPosition(Vector3 worldPos)
        {
            transform.position = worldPos;

            if (!enableTrail) return;
            EnsureTrail();
            if (_trail == null) return;

            if (_trailPoints.Count == 0 || (worldPos - _trailPoints[_trailPoints.Count - 1]).sqrMagnitude >= (trailMinDistance * trailMinDistance))
            {
                _trailPoints.Add(worldPos);
                if (trailMaxPoints > 1 && _trailPoints.Count > trailMaxPoints)
                    _trailPoints.RemoveAt(0);

                _trail.positionCount = _trailPoints.Count;
                _trail.SetPositions(_trailPoints.ToArray());
            }
        }

        public void ApplyRotation(Quaternion worldRot)
        {
            transform.rotation = worldRot;
        }

        public void Bind(int unitId)
        {
            UnitId = unitId;
            gameObject.name = $"Unit[{unitId}]";

            _renderer ??= GetComponent<Renderer>();

            EnsureForwardIndicator();
            EnsureTrail();
        }

        private void EnsureForwardIndicator()
        {
            if (_forwardIndicator != null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Forward";
            go.transform.SetParent(transform, worldPositionStays: false);

            // Small thin box in front of the unit (local +Z is forward)
            go.transform.localPosition = new Vector3(0f, 0f, 0.6f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(0.15f, 0.15f, 0.6f);

            // Make it visually distinct
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(Shader.Find("Standard")) { color = unitColor };
            }

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _forwardIndicator = go.transform;
        }

        private void EnsureTrail()
        {
            if (!enableTrail) return;
            if (_trail != null) return;

            _trail = gameObject.GetComponent<LineRenderer>();
            if (_trail == null) _trail = gameObject.AddComponent<LineRenderer>();

            _trail.useWorldSpace = true;
            _trail.alignment = LineAlignment.View;
            _trail.textureMode = LineTextureMode.Stretch;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;

            _trail.widthMultiplier = Mathf.Max(0.001f, trailWidth);
            _trail.numCapVertices = 4;
            _trail.numCornerVertices = 2;

            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = trailColor;
            _trail.material = mat;

            _trail.positionCount = 0;
            _trailPoints.Clear();
        }

        public void ApplyPartition(int px, int py, int cellSize)
        {
            partitionX = px;
            partitionY = py;
            partitionCellSize = cellSize;
        }

        // Back-compat
        public void ApplyEnemyPartition(int px, int py, int cellSize) => ApplyPartition(px, py, cellSize);

        public void ApplyTopActivity(string activityName)
        {
            topActivity = activityName;
        }

        public void ApplyActivityStack(string[] stackTopToBottom)
        {
            activityStack = stackTopToBottom;
        }

        public void ApplyAbilities(string[] abilityTypeNames)
        {
            abilities = abilityTypeNames;
        }

        public int ChildCount => childCount;
        public string[][] ChildAbilities => childAbilities;

        public void ApplyChildrenDebug(int? newChildCount, string[][] newChildAbilities)
        {
            childCount = newChildCount ?? 0;
            childAbilities = newChildAbilities;
        }

        public string RootArchetypeId => rootArchetypeId;

        public void ApplyRootArchetypeId(string id)
        {
            rootArchetypeId = id;
        }

        public int CurrentHp => currentHp;
        public int MaxHp => maxHp;

        public void ApplyHealth(int current, int max)
        {
            currentHp = current;
            maxHp = max;
        }
    }
}
