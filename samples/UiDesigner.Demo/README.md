# UiDesigner.Demo — the visual designer

A designer built on all three families at once: a welcome screen that opens a project, an infinite
canvas holding live Avalonia controls, a property inspector, a toolbox you drag from, and the project
machinery to restore, build and run what you are designing.

```bash
dotnet run --project samples/UiDesigner.Demo
```

That opens the welcome screen: the projects you had open, and Open for one that is not among them.
Pass a project to skip it, and a form name to open one straight away — the output pane is mirrored to
standard output, so this doubles as a smoke test:

```bash
dotnet run --project samples/UiDesigner.Demo -- C:\src\App\App.csproj MainView.axaml
```

It needs **both** `ArxisStudio.ProjectSystem` and `ArxisStudio.Markup` checked out beside this
repository: the demo references ProjectSystem's projects by source, and ProjectSystem's adapter
references Markup the same way. The project file fails with `SURFDEMO01` and a sentence saying so if
the first is missing; the adapter says it for the second. The Surface libraries themselves need
neither — only this demo does.

It was `samples/FormsDesigner` in ArxisStudio.ProjectSystem until it moved here, next to the library
whose canvas it is built on (ADR 0021). The comments in its code are in English for that reason, and
stay so.

It is a demonstration of a designer, and what was not that has been taken out: the project
templates, New Project and Clone from Git on the welcome screen, its Templates and Learn sections,
the Problems and Packages panes of the dock. So have the things that were only drawn — a Settings
line and an account at the foot of the welcome screen, a size chip with a chevron that opened
nothing, a ticked box beside the selection's name, and a status bar that said "Avalonia 12.1.1 ·
net10.0 · UTF-8" about every project without asking the project. What a build or a load complains
about is written to the Console, where the count of it already was.

## The design

The arrangement is the ArxisStudio UI mockup's: a 42px toolbar with Run centred, a 262px column of
Hierarchy over Toolbox, the canvas under a breadcrumb and a Design/XAML/Split switch, a 212px dock of
Project and Console, a 302px Inspector grouped into Layout, Appearance and Content, and a 26px status
bar.

The palette, the type and the sizes are not the mockup's any more. They are ArxisStudio's design
system's, which the mockup predates and did not pass:

- **Colour is a role, and the numbers are the studio theme's.** `Views/Theme.axaml` names what a
  colour is for — a surface, a plate under the pointer, three levels of text, a border a control is
  recognised by — and takes each value from the same role in `ArxisStudio.Themes.Arxis`. The accent
  is three tokens rather than one, because one colour cannot be a glyph, a fill under white text and
  a word at once; a state has a glyph colour and a text colour for the same reason. Copied rather
  than referenced: the theme would be a third repository to build beside.
- **`--verify` measures the copy.** Sixty-odd pairs — every text level on every surface it is drawn
  on, white on every fill, every glyph and border — against the thresholds the design system sets,
  in both variants. The mockup's tertiary text stood at 3.5:1 on the dark panel and 2.2:1 on the
  light one where 4.5 is asked; a field's border at 2.1:1 where 3 is asked.
- **Type is whole sizes**: 13 for what is read, 12 for what is small, 11 for a caption.
- **Nothing pressed is shorter than 24**, and everything pressed answers the pointer, the press and
  the keyboard, and says when it is switched off.
- **Everything pressed can be reached without a pointer.** The welcome screen's recent projects, the
  form tabs and the project's files were borders with a press handler; they are buttons and lists
  now. `Tab` reaches them, the arrows walk the lists, `Enter` opens.
- **The interface is in English**, all of it. It was English in the panels and Russian in the menus.

Both variants ship, keyed by `ThemeVariant`, and the toolbar switches between them — including the
canvas grid, whose colours are tokens under the keys `ArxisStudio.Surface` reads. `--theme light`
opens in the light one. Every colour in the window is a token; there is no literal outside
`Views/Theme.axaml`.

Three things the restyle found that were not about taste, each of which had been drawing the wrong
thing without saying so:

- **A rule scoped by an ancestor's class outranks the markup it is written for.** `.chrome TextBlock`
  set the text colour of everything under a panel, and a selector that depends on an ancestor is
  applied as a trigger — stronger than a value a data template sets through a resource. So every
  `Foreground="{DynamicResource Fg3}"` inside a list was written and ignored: the recent projects'
  paths, the types in the hierarchy and the inspector's headings were all drawn in the primary
  colour. The default is inherited from the chrome's root now, which is the weakest a value can be.
- **`accent` is the theme's class, not this window's.** A button carrying it is painted in the
  machine's own accent colour whatever the style here says. The class is `primary`.
- **A recent project's tile changed colour between runs.** Its hue came from `string.GetHashCode`,
  which is seeded per process.

The icons are the mockup's own paths, in `Glyphs`, together with the hue each kind of control is
drawn in — layout panels one colour, text entry another, lists a third. That is what lets a tree of
thirty rows be read by shape and colour before any of the names are.

### Checking it against the design

```bash
dotnet run --project samples/UiDesigner.Demo -- <project> <form> --shot out.png
```

Opens, waits for the window to settle, writes exactly what is on screen, and exits. A designer is a
thing you look at, so "does it work" and "does it look right" are different questions and only the
first was ever answerable from a log.

### The two that used to not match

Both are closed, and both were closed by measuring rather than by reasoning — which is worth
recording, because in each case the reasoning had been confidently wrong.

**The window no longer keeps the system title bar.** Avalonia 12 removed
`ExtendClientAreaChromeHints`; the replacement is `Window.WindowDecorations`, and the value that
fits is `BorderOnly` — the frame stays, so resizing, snapping and the shadow are still the
platform's, and only the caption goes. The toolbar is then the title bar, which costs four handlers
in the code-behind: minimise, maximise, close, and a press that calls `BeginMoveDrag`. That last one
has a trap in it. Testing `e.Source is Border { Name: "TitleBar" }` never matches, because the
toolbar's own `Grid` covers it completely; the question to ask is not "is this the bar" but "was
anything clickable under the pointer", so the handler walks up from the source and stops at the
first `Button`, `TextBox`, `ComboBox` or `MenuItem`.

**A form that sets no `Background` now shows the dark card the design shows.** This one was never
a limitation at all. The card was always the right layer — painting it magenta filled exactly the
rectangle in question — and the token was always reaching it. Every reading that said otherwise came
from a run started with `--no-build` against a binary the failed build had not replaced. With the
build confirmed, `{DynamicResource Bg1}` on the card panel renders dark, and the palette of hard
values written to work around it has been deleted.

Two lessons, and the second is the sharper one. A rendering question is answerable in about a
minute with `--shot` and unanswerable for an hour without it. And a measurement taken against a
binary you did not watch get built is not a measurement — it is the previous answer, repeated back
convincingly. The second finding above was invented entirely by that mistake.

### The header

The design's header is a 42px row, and it is reproduced control for control: the 24px mark, the
project, the branch, the configuration, then search, the run bezel, settings, a rule, and the
window's three buttons.

Two of those read state nothing else in the window has. The **branch** is asked of `git` rather
than parsed out of `.git`, because a head can be a symbolic ref, a detached hash, or a worktree
pointing elsewhere, and `git` already knows the difference; a project outside a repository shows no
branch at all rather than a plausible-looking `main`. The **run bezel** is two different sets of
controls — run, rebuild and a target while idle; a counting badge, restart and stop while
something is running — and the target it names is a runnable project, decided by the same test the
run path applies, which is that the project produced a runtime configuration.

Two deliberate departures, both so that nothing is lost rather than for their own sake. The
mockup's settings icon is decorative; here it opens the menu holding the theme, the two snap
toggles and restore, which is where the controls the header no longer shows individually went. And
the mockup's running bezel has a pause button, which is omitted: a `dotnet` process cannot be
paused, and a button that cannot do its job is worse than no button.

The keyboard reaches what the toolbar no longer shows: `Ctrl+O`, `Ctrl+S`, `Ctrl+N`, `Ctrl+B`, `F5`.

## Who does what

The division is the whole point, and none of the three knows about the others.

| | Responsibility |
| --- | --- |
| `ArxisStudio.ProjectSystem` | which project is open, which files it contains, what it resolves to, restore, build, run — and, through its adapter's design host, the project's types: built after a saved class, swapped in place, a restart asked for when they will not go |
| `ArxisStudio.Markup` | the document: parsing it, **every** edit to it, and building the live objects |
| `ArxisStudio.Surface` | the surface: viewport, grid, selection, handles, gestures — the form designer is `ArxisStudio.Surface.UiDesigner` |

Every gesture takes the same route. The editor reports it, the view model turns it into a document
edit, and the live tree is rebuilt from the document. **The document is the truth and the canvas is a
view of it** — a canvas that could disagree with the file is a designer that loses work.

## What each part is a view of

| Panel | The API behind it |
| --- | --- |
| Project | folders on the left, the folder's files on the right; **double-click a file to open it, or stop on it with the arrows and press Enter**. The tree is every item the project compiles, so a `.cs` file is there and says why it does not open; the forms are `ProjectSnapshot.Items` filtered to markup, and a file the project does not compile is correctly absent |
| Toolbox | markup snippets, dragged with Avalonia's own drag-and-drop. Where a drop lands is the editor's answer, `UiDesignerView.TryResolveDropPlacement`, shown under the pointer while dragging by `ShowDropIndicator` — a line between two neighbours of a StackPanel, by the rule the canvas reorders by; a Grid's cell; a Canvas's area; an empty Border, content control or tab page. The window only says whether, and the drop writes the same answer: the snippet becomes a Markup fragment in Avalonia's namespace, its place is set on the fragment's root (`Canvas.Left`, `Grid.Row`) and it goes in front of the neighbour the editor named. By rectangle rather than by hit-testing, for the same reason the editor decides selection that way: a loaded form takes no input, and a panel that paints no background renders nothing to hit |
| Hierarchy | both directions. The canvas reports a selection through `SurfaceSelectionChanged` and the object map turns the control into an element; the tree returns the favour through `UiDesignerView.SelectTarget`, which the map answers the other way round. That method did not exist until this sample needed it |
| Rulers | two `SurfaceRuler` controls beside the editor, told only which editor they belong to; they take the zoom and the offset from it. What they produce is a request, so the set of guides lives in the designer and nothing reaches the document |
| Canvas | one `UiDesignerItem` per open form in `ContentMode="Annotated"`, and the item's content is the form and nothing else. In `ContentMode="Loaded"` the editor offers every control the author wrote as a target and finds them by walking the container's content, so a caption or a title bar put in there is — correctly, and unhelpfully — something the user can select and resize. The card is the container's own `Background`, `BorderBrush` and `CornerRadius`; everything else the designer draws sits in a layer above the canvas, in world coordinates, through the editor's public `ViewportTransform`. What is editable is stated rather than guessed: after every load the object map says which controls have a document element behind them, and those are marked `Layout.IsTracked` |
| Inspector | the properties a control actually has, offered whether or not the document has written them — a short curated list per group, each name asked of the control first, so a Border is offered `CornerRadius` and a TextBlock is not, plus whatever the parent attaches (`Canvas.Left` inside a Canvas) and anything else the file says. Clearing a field removes the attribute. One editor per kind of value, which the document cannot decide and `XamlMemberDescriptor` can: a `bool` is a checkbox, an enum is its own values (side by side when there are four or fewer, a drop-down when more), a brush shows its colour, a number is a number. `ConvertFromText` is asked before anything is written, so text the load could not have meant is reported instead of saved. A value that is a binding is shown and not edited — typing a literal over one would replace it with whatever the text looked like. Width and Height have a `NaN` button beside the field, which takes the number out: a control with a size of its own cannot stretch, and no size is how Avalonia says the layout decides. Choosing `Stretch` for an alignment lets go of that side's size in the same edit — horizontal of the width, vertical of the height — so it is one step to undo. Under the properties, a Data section names the data type in scope and the design data the canvas shows bindings with, and lists what a binding can read; a property's row offers the members its type can take and writes `{Binding Member}`, a binding whose path names nothing on the data says *broken*, and *Create* writes `Design.DataContext` when a data type has none. All of it is asked of Markup by name, so the inspector holds no type of the project's code |
| Resize | `EditCompleted` on release, written to the element the control came from — and for the card itself, to the document's root, because a gesture on the card is a gesture on the form. A control with nothing behind it says so instead of skipping the write in silence |
| ▶ / ■ | `OutputArtifactKind.Assembly` and `RuntimeConfiguration`, built through the design host first and started from a copy of what it built |
| Console | everything above, said out loud |

## The gestures

Configured as `ArxisStudio.Surface` documents them, mapped for a form designer.

| Gesture | What it does |
| --- | --- |
| drag | moves the control under the pointer |
| `Ctrl` + drag | moves or resizes the **form** instead — that is what `ContainerInteractionModifiers` is for |
| drag on empty space | marquee selection |
| `Shift` + click | adds to the selection |
| middle drag | pans; wheel zooms |
| arrows / `Shift` + arrows | nudge by 1px / 10px, and each press is one edit |
| `Alt` | bypasses snapping for the length of one gesture |
| `Delete`, `Esc`, `Ctrl+A` | remove, deselect, select every form |
| `Ctrl+Z`, `Ctrl+Y` | back through the documents, and forward |
| `Ctrl+C`, `Ctrl+X`, `Ctrl+V`, `Ctrl+D` | copy, cut, paste, duplicate — copies carry no names |
| `Ctrl+S`, `Ctrl+Shift+S` | save this form, save every form that has edits |
| `Ctrl+0` | fit the form to the window |
| `Alt+Up`, `Alt+Down` | move the control among its siblings — one history step each |
| drag off a ruler | pulls out a guide; dragging one off the canvas is how it goes away |
| `Ctrl+G`, `Ctrl+Shift+G` | group the selection, and take the outermost level off again |

`Ctrl+Z` and `Ctrl+Y` reach the editor first when the pointer is over the canvas, and it asks rather
than acts: history belongs to whoever keeps it, and here that is the document. Answering the request
is what makes the shortcut work there; unanswered, it bubbles to the window's own key bindings,
which is why it worked before anything answered.

**Rulers and guides.** The rulers stand beside the editor rather than inside it, which is the
library's rule and not a layout preference: the editor's input point is its viewport's origin, and a
strip taking the top or the left from within its template would move that origin and all three
coordinate systems with it. A guide is dragged off a ruler, moved by dragging it, and removed by
dragging it off the canvas — each of those is a request the designer answers, and the set belongs to
the designer. Guides are **not** written to the document and are not meant to be: a guide is
scaffolding for the person laying a form out, not a fact about the form, and nothing in a project's
markup describes one. Three separate switches, because people reach for them separately — hide the
rulers, hide the lines, clear the set — and hiding never stops the pull to a line, exactly as hiding
the grid does not.

**Groups, and where one is written down.** A group is a mark on the controls rather than a node in
the tree — the editor does not write the tree, and a container it invented would change what the
canvas shows without changing the file. The mark is a path, outermost first, and the editor keeps it
in an attached property of its own.

Where it is written is the design decision. That attached property belongs to
`ArxisStudio.Surface.UiDesigner`, and a project being edited has never heard of that assembly: an `xmlns`
naming it would compile here, where the studio has it loaded, and fail the user's own build with an
unresolvable assembly. So the mark goes in the design-time namespace, beside the `d:DesignWidth`
every template already carries — attributes the XAML compiler skips because `mc:Ignorable` says to.
The declarations are added if the document lacks them, once per edit. A project carrying
`d:DesignGroup="group-1"` builds and runs exactly as before, which the stress harness checks by
building it.

The cost of that choice, stated rather than discovered: the runtime loader skips those attributes
too, so a live control does not receive its group by being loaded — the designer puts it back after
every load, the same arrangement `Layout.IsTracked` is under. And the whole group is written in one
edit, because the first write replaces the document and the live tree with it: a second write naming
a control of the tree that has just gone lands nowhere.

Snapping is on — to the grid and to the neighbours' edges and centres — and both are checkboxes on
the toolbar. Resize is contained to the form, because a button hanging outside the window it belongs
to is a picture of something that cannot happen at runtime.

**Dragging within a flow layout only works because this application answers.** The editor reads the
control tree and never writes to it, so a reorder is a request: until something marks it handled, no
insertion point is drawn and nothing moves. The handler is here and what it does is edit the
document. It uses the request's `Anchor` rather than its index, because the editor counts a panel's
children and the document counts its content elements, and the two disagree the moment a parent
holds a property element — which a `Grid` with row definitions does.

## The XAML pane

`AvaloniaEdit`, read-only, with line numbers and the editor's own XML rules. It used to be a text
block with nowhere to scroll sideways, so a document whose attributes ran past the pane's width was
simply cut off — and every one of them does.

Only the colours are replaced, and they come from the palette's own roles for code, so the pane
follows the light and dark variants with everything else. The addresses in the namespace
declarations are coloured too: the editor draws every link it finds in a pure blue of its own, which
stood at 1.9:1 on the dark pane. Two things about that are worth
knowing, because both cost an attempt: a window that is not attached yet finds none of the
application's resources, so the painting happens on `Opened` rather than in the constructor; and the
palette lives in theme dictionaries, so a lookup that does not say which variant it wants finds
nothing in one.

It stays read-only on purpose. The document is edited through the canvas and the inspector, and a
pane that also accepted typing would be a second editor of the same file with no answer for what
happens when both change it.

## Working on an application in it

The demo opens a project and does not make one: bring your own Avalonia application, or let the check
below write a small one into a folder and open that.
(`ProjectScaffold` is what writes it — a real application, `csproj`, `Program`, `App`, a window and a
view model — and the checks are its only caller.)

From there it is the designer: drag controls from the toolbox onto the form, arrange them, edit
their properties in the inspector, `Ctrl+Z` and `Ctrl+Y`, copy, cut, paste and duplicate, save, and
press Run.

The plus over the hierarchy adds a form, and asks which kind: a window is something a user is shown,
a user control is something a window is made of. Both come with an `x:Class` and a code-behind file,
because a form without one cannot be shown by the application that owns it — `new SettingsWindow()`
needs a type — and that class is what the other editor writes code in, which is the point of the two
tools sitting side by side.

Two things had to be true for that to work at all, and both are about a window-rooted form — which
is what an application's main form is:

**A project that has never been restored cannot be built**, and the designer builds what is out of
date when it opens a project, before any form names a type. So the restore comes first, and only when
the build was going to happen anyway.

**A window is whole while it is being updated.** The card shows a window by taking its content
out of it; an update reads the live tree to work out what to change, and a live window with no
content had nothing to change — so the document gained a control, the canvas did not, and the next
drop had no container to land into. The content goes back for the length of the update and is
borrowed again afterwards.

### Beside another IDE

The designer is meant to sit next to Rider or Visual Studio: the layout is done here, the code is
written there, and the same files are open in both. Six things follow from that, and the first three
were reported as bugs before they were features.

**It builds into `bin/ArxisStudio` and `obj/ArxisStudio`.** Two tools cannot own `bin/Debug`: the
moment one of them has the application running, the other's build stops with a dozen lines of
MSB3026 and then MSB3027 — "the file is locked by .NET Host" — which is true, unactionable, and looks
like the designer is broken. Nor `obj/Debug`: Rider saves before it runs, the designer hears the save
and builds too, and two builds writing one intermediate folder fail one of them. The properties are
ProjectSystem's `MSBuildDesignOutput`, passed to the evaluation as well as to the build, because the
run path starts what the evaluation says the project produces. The output paths move and the base
paths do not: the SDK keeps everything under them out of the project's globs, so the IDE's output is
never an item here, and the restore — `obj/project.assets.json` — is the one both tools read. The
designer restores first only when it has to: a project never restored, or one whose project file or
imports changed; a restore's own output changing is a restore having run.

**It follows the files.** Watching is ProjectSystem's: its source watcher reports each change with
its kind, a coalescer nets a burst into what it amounts to — Rider's save through a temporary file
and two renames is one change of the saved file, a move between folders is a rename — and the
snapshot classifies the batch. A saved form is applied to its tab as a document update, so the
session, the canvas and the tabs survive and the change joins the undo history — an edit made in the
other editor can be taken back here. A saved class is built. A file appearing or going away where a
project's globs reach re-reads the workspace, once, because an SDK project takes its items from
globs; a file merely saved does not. A form renamed or moved in the other editor — a folder renamed
around it included — follows its file and keeps its history; a form deleted while it is open here
closes its tab, rather than leaving one editing a document with nowhere to save to. A build writing
`bin` and `obj`, a tool's state in a dot-directory and an editor's temporary files come to nothing,
and not by name: the snapshot knows where its projects build and what they declare.

**It does not overwrite your unsaved work, and does not lose the file's.** A form with edits that
are not in the file is not reloaded behind them: a bar above the canvas says the file changed and
offers both ways out. *Take the file* shows it and leaves the form saved; *Keep mine* keeps the edits,
which now read as changed against what the file says. Either answer is a step of the form's history,
so a file taken over your edits leaves them one undo away. A save of the designer's own, coming back
through the watcher late, is recognised as its own and asks nothing.

**A saved class is built, and its types are swapped in place.** The project's types are
ProjectSystem's design host's (its ADR 0028), and this designer is a thin host over it (ADR 0026
here). The IDE saves a class; the host waits for the code to be quiet for 400 ms, builds the top
project of what changed — the application, for a change to the library it references — and
replaces the types this process holds with what the build wrote, without a restart. An application
and its libraries are one generation, every assembly loaded once. Nothing outside the designer
holds a swap off: not the window being behind the IDE's, and not the application started from
here running — the types replaced are the designer's, not the application's. What does hold one off
is the designer's own unfinished business — a gesture on the canvas, a value half typed in the
inspector, a dialog, a drag from the toolbox — and the swap runs the moment the last of them lets
go. Meanwhile the canvas holds the frame it last drew (`SurfaceView.Freeze`), and afterwards every
form is back as it was: the tabs, the one in front, its unsaved text and its history, the
selection and the zoom.

**The application runs from a copy.** ▶ builds through the host and starts what it built from a
copy under `%TEMP%/UiDesigner.Demo/run/<project>/<n>`, deleted when the application exits. The
next build — the next class the IDE saves — writes over nothing the running application holds, so
it builds, the types are swapped, and the application goes on running through both.

**When the old types will not go, it restarts itself, with what was open.** A swap proves the old
types have left the process before it makes the new ones, and a project whose code keeps them — a
control subscribing to something the process keeps, most often — gets a restart instead, with the
canvas holding its frame until then. Behind another window the designer waits: a designer that
vanished and came back under the hands of somebody working in the IDE would be deciding something
that is theirs. In front, and with nobody touching it for a second, it starts its next copy
(`--resume`) and hands it the session as text: the project, every open form's text and what its
file held, its place and selection, the tab in front and the zoom. The new copy opens the forms
with that text, asks each file whether it moved on meanwhile — a conflict if it did, never an
overwrite — and confirms by deleting the handoff; only then does the old copy leave. A copy that
does not confirm leaves the old one where it was, with its forms back so that what was typed can be
saved. The undo history does not cross. Three restarts in a row that each found the types held stop
it restarting by itself, and the Restart button stays.

### The check that says it works

```bash
dotnet run --project samples/UiDesigner.Demo -- --verify <folder>
```

Writes a small project into the folder, opens it through the welcome screen's Open, opens its
window, drops three controls from the toolbox into it — and four more where the editor's indicator
says: between two neighbours, onto a tab page, into a Grid's cell and at a Canvas's point, each asked
of the document and the canvas and undone again — edits one through the inspector's own rows,
gives a control a width and takes it back out three ways, duplicates and pastes a control, undoes and
redoes every step, saves, restores, builds, runs the result and stops it. Every step goes through the
view model rather than around it — a check that wrote the markup itself would only prove that the
check can write XAML.

It ends with one line: `VERDICT ok — a project was created, laid out, built and run`. Both of the
window-rooted defects above were found by it.

### The check that it keeps up with an IDE

```bash
dotnet run --project samples/UiDesigner.Demo -- --live <folder>
```

Plays the other editor. Writes a project into the folder, opens its window, and saves the form's file
the way Rider's safe write does: the text to a temporary file, the original renamed away, the
temporary renamed over it, the original deleted. A save of a clean form has to reach the canvas as
one undo step and leave the form saved; a save over unsaved edits has to change nothing until it is
answered, and both answers have to leave the text, the saved state and the canvas agreeing. A member
bound from the inspector has to show the design data's value, and read as broken once the other
editor renames it; design data taken out on disk has to be writable back from the inspector. Then
the project: a save of the form must not read the project again, a file the IDE writes must read it
once, and after an ordinary `dotnet build` has filled `obj/Debug` the designer's own build — set off
by the IDE saving the window's code — has to pass, and pass again while the application that build
produced runs from `bin/Debug`. Then the code: the view model saved in the IDE has to be built and
swapped in by itself, with no activation of the window between the build and the swap, the tabs,
the unsaved text and its history, the selection and the zoom kept, and the canvas frozen for the
swap alone; a gesture held on the canvas has to hold the swap off and get it the moment it lets go;
and the application started from the designer has to run on through a build and a swap. Then a
solution of an application and the library it references: one generation, the library loaded
once, and the library's saved class built through the application and swapped in. Last, a control
that subscribes to the process: the swap is found held, the designer waits while it is behind
another window, and in front it restarts by itself — the new copy, asked through its automation
channel, has to have the tabs, the unsaved text, the selection and the zoom. Each swap says its
phases on a `timing` line. It ends with `VERDICT ok — the designer followed an IDE writing its
forms and its code`.

## Five things it demonstrates on purpose

**A click on screen finds the line that drew it.** `XamlObjectMap` runs both ways. Templates produce
controls no element made — a button's own border, the text inside it — so the walk goes up through
the parents until it finds one that is mapped, which is how clicking a button's label selects the
button.

**The inspector offers a short list and asks the control about every name on it.** A `Button` has
upwards of a hundred properties and listing all of them tells nobody which ones matter — but an
inspector that shows only what the file already sets is a text editor with extra steps, because
nothing new can be added without typing the property name. So there is a curated list per group,
every name on it is offered only when the member resolver says this control has it, and anything the
document sets that is not on the list is appended.

**A move is written only where a position means something.** Inside a `Canvas` it becomes
`Canvas.Left` and `Canvas.Top`. Inside a `StackPanel` there is nothing to write — the panel owns the
position — so the designer says so and writes nothing, rather than faking it with a margin that
changes meaning the moment somebody adds a sibling.

**Deleting is a request, not an action.** The editor never edits the tree; it raises
`DeleteRequested` and does nothing until a subscriber marks it handled. That subscriber is here, and
what it does is edit the document.

**An edit is applied to the document first and to the live objects second.** `ApplyDocumentUpdateAsync`
works out what actually changed, so setting one property does not tear down the form around it.

**Structure is edited in words, not gestures.** Move up, move down, wrap in a container, unwrap —
on the canvas's and the hierarchy's context menus. Each is a single document edit and so a single
history step: a wrap is `ReplaceElement` over the element's own span, which cannot half-happen, and
an unwrap that would lift three children into a parent that holds one is refused in words rather
than written into the file for the loader to refuse later.

**Undo is documents, not operations.** Every edit produces a whole new immutable document, so the
history is the documents themselves and going back is applying one the designer was already holding
— through the same path every other change takes. Nothing has to be right about inverting an insert.
And the selection is a `XamlElementPath` rather than an element or a control, because both of those
are replaced by the edit: it survives, and when what was selected has just been deleted the path's
parent is the answer.

## What it is not

**A project is built when it is opened, as far as it is out of date.** A document naming an
`x:Class` names a type, and a type in a project nobody has compiled does not exist — so opening a form
in a freshly created application used to fail with "unable to resolve type", which is true and
unactionable. The design host builds what is out of date before it loads the types, and says so.

**A document is parsed with the `avares` URI it will be embedded under.** Without one a relative URI
means nothing, and a new Avalonia window says `Icon="/Assets/avalonia-logo.ico"` on its second line.

**The editor's theme has to be merged, or there is no editor.** Each of the three assemblies ships
its own theme — the core its grid and surface, the tools the selection adorners and guides, the form
designer its template — and the form designer's one entry point merges all three:

```xml
<ResourceInclude Source="avares://ArxisStudio.Surface.UiDesigner/Themes/UiDesignerTheme.axaml" />
```

Without it the control has no template and draws nothing — no grid, no forms, no handles — while
every log line still says the document loaded, because it did.

And the design's `Views/Theme.axaml` is merged *after* it. Merged dictionaries are searched from the
last to the first, so the later one wins where both declare a key — the canvas grid's colours, here.
The other order compiles, runs, and draws the library's defaults; `--verify` asks for the grid's
colour in both variants, because nothing else would say.

**A window is drawn, not hosted.** A `Window` cannot be a child of anything — Avalonia gives one a
`TopLevelHost` parent the moment it is constructed, and putting it in a `ContentControl` throws
*during layout*, off the stack of everything that could report it, so the canvas simply stayed empty.

This sample answered that itself for a while: take the content out of the window, show that, paint
the title bar, and carry the data context across by hand because `Design.DataContext` is a property
of the root and content taken out of the root loses it. Then it was a control from Markup nested
inside the editor's container, which moved the answer to the right hands and left three things to
keep in step here: the card against the stand-in inside it, the card's size against the root's, and
the title bar, drawn as a layer of its own over the canvas.

It is the editor's own container now — `UiDesignerFormItem`, ADR 0020 of ArxisStudio.Surface. The
card *is* the stand-in: it takes the document's root as an object, borrows what a window will not
share, and draws the window's title bar itself, above its own bounds and in the form's theme.
`FormViewModel` holds that card and sets its `Root` after every publication, and
`FormsDesignerView` — one override — hands it to the editor as the form's container. What is left
here is the caption above the form and the message when something went wrong.

The window has one corner radius, the card's: the title bar takes its top corners and the form
under it the bottom ones, so the two meet in a straight line. There is no second number for the
bar to disagree with — for a moment there was, and the form's rounded top left a wedge of canvas
under the bar's straight edge.

Two things the move taught, both now written where they happened. The card's size is the form's,
so it arrives from the root one side at a time — and a preview that wrote both sides back on either
change overwrote the side that had not arrived. And the designer's own card colour is the
container's `Background`, under the form's: a form that declares none shows the card, not the grid.

**Documents load in `XamlLoadMode.Design`.** A form is usually a page of bindings with nothing bound
at rest; the Avalonia template's window is one `TextBlock` reading `{Binding Greeting}`. Design mode
is what applies `Design.DataContext` and the `d:` attributes the document supplies for exactly this.

**The toolbox is Avalonia's controls, not the project's.** Reflecting over the project's assemblies
for placeable types is a real feature and a different one: it needs a rule for what counts as a
control somebody would place, and sensible initial markup per type.

**Properties are added by name.** The assembly context is right there and a typed editor per property
kind could be built on it. A name and a value is enough to show that the edit reaches the file.

**Undo is the form's, not the designer's.** Each form keeps its own history in Markup's live
document: an edit made here, a save made in the other editor and either answer to a conflict are
steps of it. Undo in one tab never reaches into another, and there is no history across forms. A
swap of the types keeps it; a restart does not carry it across.
