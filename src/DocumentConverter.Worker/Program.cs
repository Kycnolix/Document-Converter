using DocumentConverter.Worker;
using DocumentConverter.Worker.Options;
using DocumentConverter.Worker.Services;
using DocumentConverter.Shared.Options;
using DocumentConverter.Shared.Storage;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<ConverterOptions>(builder.Configuration.GetSection("Converter"));
builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection("Mongo"));
builder.Services.AddSingleton(sp =>
    new MongoConversionJobStore(
        sp.GetRequiredService<ILogger<MongoConversionJobStore>>(),
        sp.GetRequiredService<IOptions<MongoOptions>>().Value));
builder.Services.AddSingleton<IConversionJobStore>(sp => sp.GetRequiredService<MongoConversionJobStore>());
builder.Services.AddSingleton<InputFolderProcessor>();
builder.Services.AddSingleton<LibreOfficeConverter>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
