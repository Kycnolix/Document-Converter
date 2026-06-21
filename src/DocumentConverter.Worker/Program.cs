using DocumentConverter.Worker;
using DocumentConverter.Worker.Options;
using DocumentConverter.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<ConverterOptions>(builder.Configuration.GetSection("Converter"));
builder.Services.AddSingleton<InputFolderProcessor>();
builder.Services.AddSingleton<LibreOfficeConverter>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
