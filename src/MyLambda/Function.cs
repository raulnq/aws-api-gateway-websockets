using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using System.Text;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
namespace MyLambda;

public class Function
{
    private AmazonDynamoDBClient _amazonDynamoDB;
    private string _table;
    public record Payload(string Message);
    private readonly JsonSerializerOptions _options;

    public Function()
    {
        _amazonDynamoDB = new AmazonDynamoDBClient();
        _table = "connections";
        _options = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    public async Task<APIGatewayProxyResponse> Connect(APIGatewayProxyRequest input, ILambdaContext context)
    {
        var putItemRequest = new PutItemRequest
        {
            TableName = _table,
            Item = new Dictionary<string, AttributeValue> 
            {
                { "connectionid", new AttributeValue { S = input.RequestContext.ConnectionId } }
            }
        };

        await _amazonDynamoDB.PutItemAsync(putItemRequest);

        return new APIGatewayProxyResponse
        {
            Body = "connected",
            StatusCode = 200,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        };
    }

    public async Task<APIGatewayProxyResponse> Disconnect(APIGatewayProxyRequest input, ILambdaContext context)
    {
        var deleteItemRequest = new DeleteItemRequest
        {
            TableName = _table,
            Key = new Dictionary<string, AttributeValue> 
            {
                { "connectionid", new AttributeValue { S = input.RequestContext.ConnectionId } }
            }
        };

        await _amazonDynamoDB.DeleteItemAsync(deleteItemRequest);

        return new APIGatewayProxyResponse
        {
            Body = "disconnected",
            StatusCode = 200,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        };
    }

    public async Task<APIGatewayProxyResponse> Send(APIGatewayProxyRequest input, ILambdaContext context)
    {
        var scanRequest = new ScanRequest
        {
            TableName = _table,
        };

        var scanResponse = await _amazonDynamoDB.ScanAsync(scanRequest);
        var apiClient = new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig
        {
            ServiceURL = $"https://{input.RequestContext.DomainName}/{input.RequestContext.Stage}"
        });

        var payload = JsonSerializer.Deserialize<Payload>(input.Body, _options)!;
        var message = Encoding.UTF8.GetBytes($"{input.RequestContext.ConnectionId} says {payload.Message}");
        foreach (var item in scanResponse.Items)
        {
            var connectionId = item["connectionid"].S;
            var postMessageRequest = new PostToConnectionRequest
            {
                ConnectionId = connectionId,
                Data = new MemoryStream(message)
            };

            try
            {
                await apiClient.PostToConnectionAsync(postMessageRequest);
            }
            catch (GoneException)
            {
                var deleteItemRequest = new DeleteItemRequest
                {
                    TableName = _table,
                    Key = new Dictionary<string, AttributeValue> 
                    {
                        { "connectionid", new AttributeValue { S = input.RequestContext.ConnectionId }}
                    }
                };

                await _amazonDynamoDB.DeleteItemAsync(deleteItemRequest);
            }
        }

        return new APIGatewayProxyResponse
        {
            Body = "message sent",
            StatusCode = 200,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        };
    }
}
