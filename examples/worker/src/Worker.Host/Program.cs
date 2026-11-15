using Pragmatic.Composition.Hosting;
using Pragmatic.Jobs;

// The generic host, with no HTTP: the generated RunWorkerAsync registers the included modules and the job
// processor, and runs until stopped. Polling every second so a scheduled job does not wait the default five.
await PragmaticApp.RunWorkerAsync(args, app => app.UseJobs(jobs => jobs.WithPollingInterval(1))).ConfigureAwait(false);
