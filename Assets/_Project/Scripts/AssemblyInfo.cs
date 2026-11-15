using System.Runtime.CompilerServices;

// Editor tests (Assembly-CSharp-Editor) call internal test hooks such as Init(), Tick() and Configure().
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
[assembly: InternalsVisibleTo("PlayModeTests")]
