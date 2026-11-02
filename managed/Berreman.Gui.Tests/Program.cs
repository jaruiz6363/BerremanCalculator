// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// Program.cs — the window suite, run as its own executable, headless.

using Avalonia.Headless;
using Berreman.Gui.Tests;

using var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(WindowTests).Assembly);

/* --guide <folder>: the user guide's screen captures instead of the tests (GuideShots.cs). */
if (args is ["--guide", var guideDir]) return WindowTests.Guide(session, guideDir);

Console.WriteLine("=== Berreman.Gui: the window, headless ===");
var h = new Harness();

WindowTests.TheWindowOpensAndEvaluatesTheNewStack(h, session);
WindowTests.LayersAreInsertedMovedAndUndone(h, session);
WindowTests.CellsAreEditedByTyping(h, session);
WindowTests.LayerPropertiesSetTheOpticAxisAndTwist(h, session);
WindowTests.MaterialsAreAddedEditedAndProtected(h, session);
WindowTests.MaterialsAreEditedAsFormsTermsAndResponse(h, session);
WindowTests.MaterialsComeFromLibraries(h, session);
WindowTests.TheSampleBrowserFindsAndOpens(h, session);
WindowTests.AThinFilmDesignerDesignIsImported(h, session);
WindowTests.ResultsShowAnyQuantityAndTheMuellerMatrix(h, session);
WindowTests.TheConoscopicMapShowsTheIsochromes(h, session);
WindowTests.TheFieldProfileShowsTheFieldsThroughTheStack(h, session);
WindowTests.UnitsConvertAndTheFileFollows(h, session);
WindowTests.TheSpectralSweepIsStartEndAndStep(h, session);
WindowTests.TheAnglesAreStartEndAndStep(h, session);
WindowTests.ACoatingIsImportedFromOpticStudioAndOptiland(h, session);
WindowTests.MediaShowOrientationOnlyWhenAnisotropic(h, session);
WindowTests.ClosingAChangedStackAsks(h, session);

Console.WriteLine($"{h.Checks} checks, {h.Failures} failures");
return h.Failures == 0 && !WindowTests.Failed ? 0 : 1;
