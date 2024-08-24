using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.wan24_AutoDiscover>("wan24-autodiscover");

builder.Build().Run();
