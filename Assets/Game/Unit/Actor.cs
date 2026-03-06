using System;
using System.Collections.Generic;
using Game.World;
using JetBrains.Annotations;
using UnityEngine;
using Game.Unit.Activity;

namespace Game.Unit
{
public class Actor
{
   public int Id;

   /// <summary>
   /// Debug-only: which YAML archetype (UnitData.Id) was used to construct this actor.
   /// Helps verify factory/archetype wiring across threads.
   /// </summary>
   public string DebugArchetypeId;

   public Stack<Game.Unit.Activity.IActivity> Activities {get; set; }
   [CanBeNull] public Actor parent;
   [CanBeNull] private List<Actor> children;

   /// <summary>
   /// Read-only view of child actors. Used by LogicWorld snapshot/debug.
   /// </summary>
   public IReadOnlyList<Actor> Children => children;

   private HashSet<IAbility> abilities = new HashSet<IAbility>();

   public HashSet<IAbility> Abilities
   {
      get { return abilities; }
   }

   /// <summary>
   /// Optional world services (logic thread) that the actor can query.
   /// Set by LogicWorld when the actor is registered.
   /// </summary>
   public IOccupancyView World { get; internal set; }

   /// <summary>
   /// If true, this actor represents a top-level gameplay unit (e.g., chassis) rather than an attachment.
   /// Used for snapshot/debug filtering.
   /// </summary>
   public bool IsPrimaryUnit;

   /// <summary>
   /// Faction/camp of this actor. 0 = Neutral by default.
   /// </summary>
   public int Faction;

   /// <summary>
   /// Owning player id. -1 = none/neutral by default.
   /// </summary>
   public int OwnerPlayerId = -1;

   /// <summary>
   /// What alert/target layer this unit belongs to (underwater/ocean/ground/low air/high air).
   /// Used by Guard/EnemySearchService filtering.
   /// Default: Ground.
   /// </summary>
   public Game.Unit.UnitAlertLayer UnitAlertLayer = Game.Unit.UnitAlertLayer.Ground;

   [Obsolete("Actor must be constructed with ownership (faction, ownerPlayerId, color).", error: true)]
   public Actor() { }

   public Actor(int faction, int ownerPlayerId)
   {
      ApplyOwnership(faction, ownerPlayerId);
   }

   public void ApplyOwnership(int faction, int ownerPlayerId)
   {
      Faction = faction;
      OwnerPlayerId = ownerPlayerId;
   }

   public void Tick()
   {
      // Tick and consume finished activities. This avoids a 1-tick gap when an activity finishes.
      // Safety: cap the number of pops per tick to prevent infinite loops if activities finish immediately.
      const int MaxActivityTransitionsPerTick = 8;
      int transitions = 0;

      while (Activities != null && Activities.Count > 0)
      {
         Activities.Peek().Tick();

         if (!Activities.Peek().IsFinished())
            break;

         Activities.Pop();
         transitions++;
         if (transitions >= MaxActivityTransitionsPerTick)
            break;
      }

      if (children!=null)
      {
         foreach (var child in children)
         {
            child.Tick();
         }
      }
   }

   // Add a child actor and set its parent reference
   public void AddChild(Actor child)
   {
      if (child == null) return;
      if (children == null) children = new List<Actor>();
      if (!children.Contains(child))
      {
          children.Add(child);
          child.parent = this;
      }
   }
}

}
