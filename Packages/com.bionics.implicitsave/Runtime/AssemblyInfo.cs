using System.Runtime.CompilerServices;

// The test suite reaches internals on purpose: the mid-write failure test needs the seam in
// FileSaveStorage, and the id rules are internal because they are not part of the public contract.
[assembly: InternalsVisibleTo("ImplicitSave.Tests.Runtime")]
[assembly: InternalsVisibleTo("ImplicitSave.Editor")]
[assembly: InternalsVisibleTo("ImplicitSave.Tests.Editor")]
