var builder = DistributedApplication.CreateBuilder(args);

// Add the CryptoReportBot project to the orchestrator
builder.AddProject<Projects.CryptoReportBot>("cryptoreportbot");

builder.Build().Run();
