using Game.Map;
using Game.Grid;

namespace Game.Unit.Activity
{
    internal static class OccupancyUtil
    {
        /// <summary>
        /// Returns true if <paramref name="a"/> should be considered the same unit as <paramref name="self"/>
        /// for occupancy blocking purposes.
        /// </summary>
        public static bool IsSameUnitActor(Actor a, Actor self)
        {
            if (a == null || self == null) return false;
            if (ReferenceEquals(a, self)) return true;

            // Treat parent/child parts of a composite unit as "self" so they don't block each other.
            if (ReferenceEquals(a.parent, self) || ReferenceEquals(self.parent, a)) return true;

            var selfChildren = self.Children;
            if (selfChildren != null)
            {
                for (int i = 0; i < selfChildren.Count; i++)
                {
                    if (ReferenceEquals(selfChildren[i], a)) return true;
                }
            }

            var aChildren = a.Children;
            if (aChildren != null)
            {
                for (int i = 0; i < aChildren.Count; i++)
                {
                    if (ReferenceEquals(aChildren[i], self)) return true;
                }
            }

            return false;
        }

        public static bool IsCellFreeOfOthers(Game.World.LayeredOccupancyIndex occ, GridPosition cell, uint movementMask, Actor self)
        {
            if (occ == null) return true;
            if (movementMask == 0u) movementMask = 1u;

            uint mask = movementMask;
            uint bit = 1u;
            while (mask != 0)
            {
                if ((mask & 1u) != 0)
                {
                    var layer = (MapLayer)bit;
                    var list = occ.GetActorsAt(layer, cell);
                    for (int i = 0; i < list.Count; i++)
                    {
                        var a = list[i];
                        if (a == null) continue;
                        if (!IsSameUnitActor(a, self))
                            return false;
                    }
                }

                mask >>= 1;
                bit <<= 1;
            }

            return true;
        }
    }
}
