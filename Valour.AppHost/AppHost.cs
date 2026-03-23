using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("valourgres")
	.WithImageTag("16.12") // pin version
	.WithDataVolume("valour-postgres-data")
	.WithPgAdmin();

var valourDb = postgres.AddDatabase("valourdb");

var redis = builder.AddRedis("redis")
	.WithDataVolume("redis-data")
	.WithRedisInsight();

var valourServer = builder.AddProject<Valour_Server>("valour-server")
	.WaitFor(valourDb)
	.WaitFor(redis)
	.WithReference(valourDb)
	.WithReference(redis);

builder.Build().Run();
