using Payments.Api;
using Payments.Api.Storage;
using Payments.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddValidation();  
builder.Services.AddSingleton(TimeProvider.System);

string cs = builder.Configuration.GetConnectionString("Postgres")!;
builder.Services.AddScoped<IPaymentStore>(_ => new DapperPaymentStore(cs, TimeProvider.System));

var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPayments();

app.Run();
