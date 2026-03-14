#if UNITY_EDITOR || ENABLE_TEST_DEBUG
using System;
using Game.Scripts.Fixed;

namespace Game.Command
{
    internal static class CommandCodecTests
    {
        public static void RunSmoke()
        {
            var cmd = CommandFactory.Move(new[] { 1, 2, 3 }, new FixedVector3(Fixed.FromInt(10), Fixed.FromInt(0), Fixed.FromInt(-5)));
            var bytes = CommandFactory.Encode(cmd);
            if (!CommandFactory.TryDecode(bytes, out var decoded))
                throw new Exception("Decode failed");

            if (decoded.Type != cmd.Type || decoded.Payload != cmd.Payload) throw new Exception("Header mismatch");
            if (decoded.UnitIds.Length != cmd.UnitIds.Length) throw new Exception("UnitIds length mismatch");
            for (int i = 0; i < cmd.UnitIds.Length; i++)
                if (decoded.UnitIds[i] != cmd.UnitIds[i]) throw new Exception("UnitIds mismatch");

            if (decoded.Point.x.Raw != cmd.Point.x.Raw || decoded.Point.y.Raw != cmd.Point.y.Raw || decoded.Point.z.Raw != cmd.Point.z.Raw)
                throw new Exception("Point mismatch");
        }
    }
}
#endif
