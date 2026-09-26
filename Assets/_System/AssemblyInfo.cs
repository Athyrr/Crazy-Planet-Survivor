using System.Runtime.CompilerServices;

// I2 fix (final whole-branch review): CrazyPlanet.Tests.Editor.asmdef referenced the predefined assembly
// "Assembly-CSharp" by name, which Unity does not allow for an asmdef-defined assembly — that reference
// was invalid regardless of this attribute's target. The asmdef is deleted; AreaAttackShapeTests.cs now
// compiles into the implicit Assembly-CSharp-Editor (no .asmdef anywhere in its folder chain), so this
// attribute's target is updated to match. Best-effort without a compiler in this session — a human must
// confirm in Unity's Test Runner that the 4 tests appear under EditMode and pass.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
