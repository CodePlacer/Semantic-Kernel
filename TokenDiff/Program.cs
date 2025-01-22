using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json.Linq;
using OpenAI.Chat;
using System.ComponentModel;
using System.Text.Json.Serialization;


const string DeploymentName = "";
const string Endpoint = "";
const string ApiKey = "";


var customHttpHanlder = new AzureOpenAIHttpHandler();
var customHttpClient = new HttpClient(customHttpHanlder);

var kernel = Kernel.CreateBuilder()
    .AddAzureOpenAIChatCompletion(DeploymentName, Endpoint, ApiKey, httpClient: customHttpClient)
    .Build();

kernel.Plugins.AddFromType<LightsPlugin>("Lights");

OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
{
    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
};

KernelArguments kernelArgs = new KernelArguments(openAIPromptExecutionSettings);

var response = await kernel.InvokePromptAsync("Please turn on the lamp", kernelArgs);



var defaultcolor = Console.ForegroundColor;
Console.WriteLine("#RESPONSE");
Console.WriteLine(response);


Console.ForegroundColor = ConsoleColor.Magenta;
Console.WriteLine("#Token Usage from Kernel Metadata");
ChatTokenUsage? _tokusage = ((ChatTokenUsage?)response.Metadata?.GetValueOrDefault("Usage", null));

Console.ForegroundColor = defaultcolor;
Console.WriteLine($"Input token : {_tokusage?.InputTokenCount}");
Console.WriteLine($"Output token : {_tokusage?.OutputTokenCount}");
Console.WriteLine($"Total token : {_tokusage?.TotalTokenCount}");


Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("#Token Usage from HTTP Client");

Console.ForegroundColor = defaultcolor;
Console.WriteLine($"Input token : {TokenMetricsHelper.chatTokenUsages.Sum(x => x.InputTokens)}");
Console.WriteLine($"Output token : {TokenMetricsHelper.chatTokenUsages.Sum(x => x.OutputTokens)}");
Console.WriteLine($"Total token : {TokenMetricsHelper.chatTokenUsages.Sum(x => x.TotalTokens)}");

Console.ReadLine();


//Custom HTTP Handler

public class AzureOpenAIHttpHandler : DelegatingHandler
{


    public AzureOpenAIHttpHandler(HttpMessageHandler innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler())
    {
    }
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Send the HTTP request
        string reqBody = await request.Content.ReadAsStringAsync();
        var response = await base.SendAsync(request, cancellationToken);


        string remainingRequests;
        string remainingTokens;

        // Check if the x-ratelimit-remaining-requests header exists
        if (response.Headers.TryGetValues("x-ratelimit-remaining-requests", out var req))
        {
            remainingRequests = req.FirstOrDefault();

            // Optional: Implement logic based on remaining requests
            // For example, pause or throttle requests if nearing limit
        }

        if (response.Headers.TryGetValues("x-ratelimit-remaining-tokens", out var tok))
        {
            remainingTokens = tok.FirstOrDefault();

            // Optional: Implement logic based on remaining requests
            // For example, pause or throttle requests if nearing limit
        }


        var resBodyObj = JObject.Parse(reqBody);

        bool.TryParse((resBodyObj.SelectToken("stream") as JValue)?.Value?.ToString(), out bool isStreaming);
        if (!isStreaming)
        {
            LogRequestAndResponse(request, response);
        }

        return response;
    }

    private async void LogRequestAndResponse(HttpRequestMessage request, HttpResponseMessage httpResponse)
    {
        string reqBody = await request.Content.ReadAsStringAsync();
        string resBody = await httpResponse.Content.ReadAsStringAsync();

        var resBodyObj = JObject.Parse(resBody);
        if (resBodyObj is not null && Convert.ToString(resBody) != "")
        {
            int.TryParse((resBodyObj.SelectToken("usage.prompt_tokens") as JValue)?.Value?.ToString(), out int prompt_tokens);
            int.TryParse((resBodyObj.SelectToken("usage.completion_tokens") as JValue)?.Value?.ToString(), out int completion_tokens);
            int.TryParse((resBodyObj.SelectToken("usage.total_tokens") as JValue)?.Value?.ToString(), out int total_tokens);
            string modelName = (resBodyObj.SelectToken("model") as JValue)?.Value?.ToString() ?? string.Empty;

            TokenMetricsHelper.TrackTokenUsage(prompt_tokens, completion_tokens, total_tokens);
        }
    }
}




//For Tracking token usage from http Response
public static class TokenMetricsHelper
{
    public static List<TokenMetrics> chatTokenUsages = [];
    public static void TrackTokenUsage(int InputToken, int OutputToken, int TotalToken)
    {
        TokenMetrics tokenMetrics = new()
        {
            InputTokens = InputToken,
            OutputTokens = OutputToken,
            TotalTokens = TotalToken
        };
        chatTokenUsages.Add(tokenMetrics);
    }
}

public class TokenMetrics
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int TotalTokens { get; init; }

}



//Kernel Plugin

public class LightsPlugin
{
    // Mock data for the lights
    private readonly List<LightModel> lights = new()
   {
      new LightModel { Id = 1, Name = "Table Lamp", IsOn = false, Brightness = 100, Hex = "FF0000" },
      new LightModel { Id = 2, Name = "Porch light", IsOn = false, Brightness = 50, Hex = "00FF00" },
      new LightModel { Id = 3, Name = "Chandelier", IsOn = true, Brightness = 75, Hex = "0000FF" }
   };

    [KernelFunction("get_lights")]
    [Description("Gets a list of lights and their current state")]
    public async Task<List<LightModel>> GetLightsAsync()
    {
        return lights;
    }

    [KernelFunction("get_state")]
    [Description("Gets the state of a particular light")]
    public async Task<LightModel?> GetStateAsync([Description("The ID of the light")] int id)
    {
        // Get the state of the light with the specified ID
        return lights.FirstOrDefault(light => light.Id == id);
    }

    [KernelFunction("change_state")]
    [Description("Changes the state of the light")]
    public async Task<LightModel?> ChangeStateAsync(int id, LightModel LightModel)
    {
        var light = lights.FirstOrDefault(light => light.Id == id);

        if (light == null)
        {
            return null;
        }

        // Update the light with the new state
        light.IsOn = LightModel.IsOn;
        light.Brightness = LightModel.Brightness;
        light.Hex = LightModel.Hex;

        return light;
    }
}

public class LightModel
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("is_on")]
    public bool? IsOn { get; set; }

    [JsonPropertyName("brightness")]
    public byte? Brightness { get; set; }

    [JsonPropertyName("hex")]
    public string? Hex { get; set; }
}
