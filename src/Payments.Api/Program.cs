using Payments.Api;
using Payments.Api.Auth;
using Payments.Api.Storage;
using Payments.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddValidation();  
builder.Services.AddSingleton(TimeProvider.System);

string cs = builder.Configuration.GetConnectionString("Postgres")!;
builder.Services.AddScoped<IPaymentStore>(_ => new DapperPaymentStore(cs, TimeProvider.System));
builder.Services.AddScoped<IApiKeyStore>(_ => new DapperApiKeyStore(cs));
builder.Services.AddScoped<IPaymentQueries>(_ => new DapperPaymentQueries(cs));
builder.Services.AddScoped<IIdempotencyStore>(_ => new DapperIdempotencyStore(cs));

var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPayments();

app.Run();
