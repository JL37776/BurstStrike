// using NUnit.Framework;
// using Game.Serialization;
//
// namespace Game.Serialization.Tests
// {
//     public class YamlUnitValidationTests
//     {
//         [Test]
//         public void TryDeserializeValidated_Ok_ReturnsTrue()
//         {
//             var yaml = @"
// Id: \"u1\"
// Health: 1
// Position: [0, 0, 0]
// AbilityParams:
//   Movement:
//     MaxSpeed: 1
//     Acceleration: 2
// ";
//
//             var ok = YamlHelper.TryDeserializeValidated(yaml, out var data, out var errors);
//             Assert.IsTrue(ok);
//             Assert.NotNull(data);
//             Assert.IsEmpty(errors);
//         }
//
//         [Test]
//         public void TryDeserializeValidated_MissingPosition_Fails()
//         {
//             var yaml = @"
// Id: \"u1\"
// Health: 1
// AbilityParams:
//   Movement:
//     MaxSpeed: 1
//     Acceleration: 2
// ";
//
//             var ok = YamlHelper.TryDeserializeValidated(yaml, out var data, out var errors);
//             Assert.IsFalse(ok);
//             Assert.IsNull(data);
//             Assert.IsNotEmpty(errors);
//         }
//
//         [Test]
//         public void TryDeserializeValidated_MissingMaxSpeed_Fails()
//         {
//             var yaml = @"
// Id: \"u1\"
// Health: 1
// Position: [0, 0, 0]
// AbilityParams:
//   Movement:
//     Acceleration: 2
// ";
//
//             var ok = YamlHelper.TryDeserializeValidated(yaml, out var data, out var errors);
//             Assert.IsFalse(ok);
//             Assert.IsNull(data);
//             Assert.IsTrue(errors.Exists(e => e.Contains("MaxSpeed")));
//         }
//
//         [Test]
//         public void TryDeserializeValidated_MissingAcceleration_Fails()
//         {
//             var yaml = @"
// Id: \"u1\"
// Health: 1
// Position: [0, 0, 0]
// AbilityParams:
//   Movement:
//     MaxSpeed: 1
// ";
//
//             var ok = YamlHelper.TryDeserializeValidated(yaml, out var data, out var errors);
//             Assert.IsFalse(ok);
//             Assert.IsNull(data);
//             Assert.IsTrue(errors.Exists(e => e.Contains("Acceleration")));
//         }
//
//         [Test]
//         public void TryDeserializeValidated_ChildMissingRequired_FailsAndReportsPath()
//         {
//             var yaml = @"
// Id: \"root\"
// Health: 1
// Position: [0, 0, 0]
// AbilityParams:
//   Movement:
//     MaxSpeed: 1
//     Acceleration: 2
// Children:
//   - Id: \"child\"
//     Health: 1
//     Position: [0, 0, 0]
//     AbilityParams:
//       Movement:
//         MaxSpeed: 1
// ";
//
//             var ok = YamlHelper.TryDeserializeValidated(yaml, out var data, out var errors);
//             Assert.IsFalse(ok);
//             Assert.IsNull(data);
//             Assert.IsTrue(errors.Exists(e => e.Contains("root.Children[0]") && e.Contains("Acceleration")));
//         }
//     }
// }
