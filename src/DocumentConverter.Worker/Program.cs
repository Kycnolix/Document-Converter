using DocumentConverter.Worker;
using DocumentConverter.Worker.Options;
using DocumentConverter.Worker.Services;
using DocumentConverter.Shared.Storage;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<ConverterOptions>(builder.Configuration.GetSection("Converter"));
builder.Services.AddSingleton(sp =>
    new ConversionJobMetadataStore(
        sp.GetRequiredService<ILogger<ConversionJobMetadataStore>>(),
        sp.GetRequiredService<IOptions<ConverterOptions>>().Value.JobsRoot));
builder.Services.AddSingleton<InputFolderProcessor>();
builder.Services.AddSingleton<LibreOfficeConverter>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
