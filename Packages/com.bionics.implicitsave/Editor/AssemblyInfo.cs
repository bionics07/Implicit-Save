using System.Runtime.CompilerServices;

// The editor test suite reaches internals on purpose: SaveProxy and the dictionary drawer's
// duplicate-key check are implementation details of the window, not part of the package's API, but
// they are exactly the parts worth testing.
[assembly: InternalsVisibleTo("ImplicitSave.Tests.Editor")]
