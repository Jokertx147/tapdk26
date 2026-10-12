using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("The Airline Project - game logic")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

// the user interface and the tests use a few internal types of the logic (for example the Translator)
[assembly: InternalsVisibleTo("TheAirline")]
[assembly: InternalsVisibleTo("TheAirline.Tests")]
