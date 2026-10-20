using Payments.Api;
using Payments.Api.Storage;
using Payments.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddValidation();  
builder.Services.AddSingleton(TimeProvider.System);

string cs = builder.Configuration.GetConnectionString("Postgres")!;
builder.Services.AddScoped<IPaymentStore>(_ => new DapperPaymentStore(cs));

var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Throwaway scaffolding. Deleted in step 13.
app.MapGet("/problems/demo/{code}", (PaymentErrorCode code, HttpContext http) =>
    ProblemMapper.ToProblem(new PaymentError(code, "a demo message"), http));

app.MapPayments();

app.Run();
