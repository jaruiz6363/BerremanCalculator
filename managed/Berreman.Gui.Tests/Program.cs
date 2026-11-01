// Program.cs — the window suite, run as its own executable, headless.

using Avalonia.Headless;
using Berreman.Gui.Tests;

Console.WriteLine("=== Berreman.Gui: the window, headless ===");
var h = new Harness();
using var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(WindowTests).Assembly);

WindowTests.TheWindowOpensAndEvaluatesTheNewStack(h, session);
WindowTests.LayersAreInsertedMovedAndUndone(h, session);
WindowTests.CellsAreEditedByTyping(h, session);
WindowTests.LayerPropertiesSetTheOpticAxisAndTwist(h, session);
WindowTests.ADesignStringBuildsLayers(h, session);
WindowTests.MaterialsAreAddedEditedAndProtected(h, session);
WindowTests.MaterialsAreEditedAsFormsTermsAndResponse(h, session);
WindowTests.MaterialsComeFromLibraries(h, session);
WindowTests.UnitsConvertAndTheFileFollows(h, session);
WindowTests.ClosingAChangedStackAsks(h, session);

Console.WriteLine($"{h.Checks} checks, {h.Failures} failures");
return h.Failures == 0 && !WindowTests.Failed ? 0 : 1;
