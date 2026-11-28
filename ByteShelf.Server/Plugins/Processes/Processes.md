# Plugins' processes

What plugins do over files' contents (`AddReader` and `AddMaker`, [Plugins](../Plugins.md)) runs in child
processes of the server: a native decoder of untrusted bytes can crash, hang or eat memory there without the
server doing so. **It is no sandbox.** The process runs as the server's user, so a plugin, or contents that take one
over, can open whatever that user can: the root, the database, other programs' files. It is given none of
them, and nothing keeps it from them.

`PluginProcesses` has a run done: the plugin's turn in the run's lane (`PluginTurns`: one thing at a time per plugin
and lane, what somebody waits for first, then making from contents just stored, then the backlog, which all reading
is), then the process (`PluginWorker`, of `PluginWorkers`), used by one run at a time, noting the run
(`PluginClaims`) so a removal of its contents finds it. Each plugin has one in each lane (`PluginLane`): the quick
lane, for reading and every derived file but a stream, and the long lane, for streams (`DerivedKinds.LaneOf`), so a
run of hours holds up none of the plugin's others. A process is the server's own executable as
`--plugin-process <folder>`, with `--lowest-priority` in the long lane
(`PluginProcessCommand`), which loads the plugin as the server does, among the server's registrations on no root,
and makes its reader and maker and nothing else. What it may be asked to do is each module's `ProcessWork`
(metadata's reading, derived files' making), which a run names by its type. It does many things, and is ended when
it has had nothing to do for 60 seconds. One that died or was ended is replaced by the next run.

## What it is given

- **The contents, a part at a time.** The two talk in frames (`Frame`) over the process's standard input and
  output, which are taken from it first (`ProtocolStreams`), so what a plugin or a native library prints or
  reads there is none of them. A read of the contents (`PipedContents`) is asked of the server, which holds the
  file: the process has neither the object store nor a link into it.
- **A folder to write in** (`WorkFolder`, under `derived/work/`), its working folder and the one for temporary
  files, emptied after each run.
- **Of the server's environment** only what the runtime and the locale need (`PluginProcess`).
- **1.5 GiB of memory** (`PluginMemory.MaxBytes`). On Linux the process limits itself before the plugin is
  loaded, and cannot take more private memory; shared mappings are not counted, and a process it starts has a
  limit of its own. Elsewhere the server watches the process, and not those it starts. The collector of the
  runtime does not run beside the plugin, where garbage could reach the limit.
- **60 seconds** a run in the quick lane, **24 hours** in the long lane, and 60 seconds to load the plugin in either.
  Over it, the process is killed, and the run has failed as any run does.
- **In the long lane, the lowest CPU priority**, so the server stays responsive: once the plugin is loaded, in the
  same 60 seconds as in the quick lane, and before any run, the process lowers itself, its group and every thread in
  it, to nice 19 (`ProcessGroup.TakeLowestPriority`), and every process it starts inherits it, as an encoder the
  plugin runs does. On Windows it takes the idle priority class.

Its memory is left out of the dumps .NET makes, and on Linux out of the system's core dumps. What it writes to
standard error is thrown away and an exception is told by its type alone, since either may quote the contents.

## What an end says

| The process | Means |
|---|---|
| answers | That is the answer. The process goes on, unless it still takes over 512 MiB (`PluginMemory.MaxKeptBytes`), which would leave the next contents too little. |
| says the plugin threw, crashes, exits, runs out of time or memory, or says what it may not, once it has begun | The contents are at fault: the run has failed, and is recorded so before anything is tried again. The process is replaced. |
| cannot load the plugin, or ends before it has begun | The contents are not at fault (`PluginUnavailableException`): nothing is recorded, and the next run tries again. So it is when the plugin is another version than the server loaded, its reader or maker cannot be made, or the disk is full, as far as an error says so (any other I/O error is as good as damaged contents). |
| is ended by the server, for other contents | Nothing: the run is done anew in another process. |
| is ended by the server, for these contents or as it stops | Nothing, and nothing is recorded: whoever had it run is told it was stopped. |
| had died as it waited | Nothing: the run goes to another process. |
| was asked over contents the server cannot open or read | Nothing, and nothing is recorded. |

It ends when the server does, however that ends, and takes the processes it started with it, as any end of it
does: it leads a process group (`ProcessGroup`), and what it starts has no end of the pipes to the server. On Windows, which has none, what a plugin started may outlive
it. A server that begins to stop ends them all first (`PluginProcessesLifetime`), and they ignore the signals
a system stops things by, so stopping is never taken for the contents' doing.

## Leaving nothing of the plaintext

A process that read contents may hold some of them in memory for as long as it lives. So when contents are to
leave nothing behind (`StopAsync`, for a file encrypted in place and for a purge,
[Derived files](../../Derived/Derived.md)) **every process that ever read them is ended**, not only one reading
them now, with its folder removed; what it was doing over other contents is done anew.

A run begins under the contents' lock (`ContentsLocks`), which whoever stops them holds until the swap has
committed, and only while a plain file holds them, which `PluginProcesses` asks itself: so none begins in
between. Beside each folder is a list of the contents its process was given (`<folder>.contents`), written
before they are, or they are not given; so one that cannot be removed is found by them, whichever server left
it, and refuses the swap until it can. A server that starts removes every folder, and daily maintenance ends and
removes whatever has read contents that no plain file holds any more (`GetContentsGiven`).
