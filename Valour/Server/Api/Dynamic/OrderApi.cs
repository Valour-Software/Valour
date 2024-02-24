using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Valour.Server.Config;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class OrderApi
{
    private static readonly HttpClient Http = new();
    
    // SANDBOX
    //const string BaseUrl = "https://api-m.sandbox.paypal.com";
    // LIVE
    const string BaseUrl = "https://api-m.paypal.com";

    // json binding for paypal oauth response
    public class PaypalAccessToken
    {
        [JsonPropertyName("scope")]
        public string Scope { get; set; }
        
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }
        
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }
        
        [JsonPropertyName("app_id")]
        public string AppId { get; set; }
        
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        
        [JsonPropertyName("nonce")]
        public string Nonce { get; set; }
    }

    public class PaypalPurchasePayload
    {
        [JsonPropertyName("intent")]
        public string Intent { get; set; }
        
        [JsonPropertyName("purchase_units")]
        public List<PaypalPurchaseUnit> PurchaseUnits { get; set; }
        
        [JsonPropertyName("soft_descriptor")]
        public string SoftDescriptor { get; set; }
        
        [JsonPropertyName("reference_id")]
        public string ReferenceId { get; set; }
        
        [JsonPropertyName("application_context")]
        public AppContext AppContext { get; set; }
    }
    
    public class PaypalPurchaseUnit
    {
        [JsonPropertyName("amount")]
        public PaypalAmount Amount { get; set; }
        
        [JsonPropertyName("items")]
        public List<PaypalItem> Items { get; set; }
    }
    
    public class PaypalItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("quantity")]
        public string Quantity { get; set; }
        
        [JsonPropertyName("category")]
        public string Category { get; set; }
        
        [JsonPropertyName("description")]
        public string Description { get; set; }
        
        [JsonPropertyName("unit_amount")]
        public PaypalBreakdownAmount UnitAmount { get; set; }
    }
    
    public class PaypalBreakdown
    {
        [JsonPropertyName("item_total")]
        public PaypalBreakdownAmount ItemTotal { get; set; }
    }
    
    public class PaypalBreakdownAmount
    {
        [JsonPropertyName("currency_code")]
        public string CurrencyCode { get; set; }
        
        [JsonPropertyName("value")]
        public string Value { get; set; }
    }
    
    public class PaypalAmount
    {
        [JsonPropertyName("currency_code")]
        public string CurrencyCode { get; set; }
        
        [JsonPropertyName("value")]
        public string Value { get; set; }
        
        [JsonPropertyName("breakdown")]
        public PaypalBreakdown Breakdown { get; set; }
    }

    public class IdObject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
    }

    public class CaptureResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        
        [JsonPropertyName("status")]
        public string Status { get; set; }
        
        [JsonPropertyName("payment_source")]
        public Dictionary<string, CapturePaymentSource> Sources { get; set; }
        
        [JsonPropertyName("purchase_units")]
        public List<CapturePurchaseUnit> PurchaseUnits { get; set; }
    }

    public class CapturePaymentSource
    {
        [JsonPropertyName("email_address")]
        public string EmailAddress { get; set; }
        
        [JsonPropertyName("account_id")]
        public string AccountId { get; set; }
        
        [JsonPropertyName("account_status")]
        public string AccountStatus { get; set; }
        
        [JsonPropertyName("name")]
        public CaptureName Name { get; set; }
    }

    public class CaptureName
    {
        [JsonPropertyName("given_name")]
        public string GivenName { get; set; }
        
        [JsonPropertyName("surname")]
        public string Surname { get; set; }
    }

    public class CapturePurchaseUnit
    {
        [JsonPropertyName("reference_id")]
        public string ReferenceId { get; set; }
        
        [JsonPropertyName("payments")]
        public PaymentCaptures Payments { get; set; }
        
    }

    public class PaymentCaptures
    {
        [JsonPropertyName("captures")]
        public List<PaymentCapture> Captures { get; set; }
    }

    public class PaymentCapture
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        
        [JsonPropertyName("status")]
        public string Status { get; set; }
        
        [JsonPropertyName("amount")]
        public PaypalAmount Amount { get; set; }
    }

    public class AppContext
    {
        [JsonPropertyName("shipping_preference")]
        public string ShippingPreference { get; set; }
    }

    public static class ValourOrderTypes
    {
        public static readonly PaypalPurchaseUnit VC500 = new()
        {
            Amount = new PaypalAmount
            {
                CurrencyCode = "USD",
                Value = "5.00",
                Breakdown = new PaypalBreakdown()
                {
                    ItemTotal = new PaypalBreakdownAmount()
                    {
                        CurrencyCode = "USD",
                        Value = "5.00"
                    }
                }
            },
            Items = new List<PaypalItem>()
            {
                new()
                {
                    Name = "500 Valour Credits",
                    Quantity = "1",
                    Category = "DIGITAL_GOODS",
                    Description = "500 Valour Credits, for use on valour.gg",
                    UnitAmount = new PaypalBreakdownAmount()
                    {
                        CurrencyCode = "USD",
                        Value = "5.00"
                    }
                }
            }
        };

        public static readonly PaypalPurchaseUnit VC1000 = new()
        {
            Amount = new PaypalAmount
            {
                CurrencyCode = "USD",
                Value = "9.00",
                Breakdown = new PaypalBreakdown()
                {
                    ItemTotal = new PaypalBreakdownAmount()
                    {
                        CurrencyCode = "USD",
                        Value = "9.00"
                    }
                }
            },
            Items = new List<PaypalItem>()
            {
                new()
                {
                    Name = "1000 Valour Credits",
                    Quantity = "1",
                    Category = "DIGITAL_GOODS",
                    Description = "1000 Valour Credits, for use on valour.gg",
                    UnitAmount =  new PaypalBreakdownAmount()
                    {
                        CurrencyCode = "USD",
                        Value = "9.00"
                    }
                }
            }
        };

        public static readonly PaypalPurchaseUnit VC2000 = new()
        {
            Amount = new PaypalAmount
            {
                CurrencyCode = "USD",
                Value = "15.00",
                Breakdown = new PaypalBreakdown()
                {
                    ItemTotal = new PaypalBreakdownAmount()
                    {
                        CurrencyCode = "USD",
                        Value = "15.00"
                    }
                }
            },
            Items = new List<PaypalItem>()
            {
                new()
                {
                    Name = "2000 Valour Credits",
                    Quantity = "1",
                    Category = "DIGITAL_GOODS",
                    Description = "2000 Valour Credits, for use on valour.gg",
                    UnitAmount = new PaypalBreakdownAmount()
                    {
                        CurrencyCode = "USD",
                        Value = "15.00"
                    }
                }
            }
        };
    }   
    
    /* No need to rebuild these for each request */ 
    private static readonly Dictionary<string, string> GrantType = new()
    {
        {"grant_type", "client_credentials"}
    };
    
    private static readonly FormUrlEncodedContent GrantTypeContent = new(GrantType);

    // Map for order ids
    private static readonly Dictionary<string, string> OrderIdsToType = new();
    
    public static async Task<PaypalAccessToken> GetAccessToken()
    {
        var auth = PaypalConfig.Current.ClientId + ":" + PaypalConfig.Current.AppSecret;
        var auth64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
        
        var request = new HttpRequestMessage(HttpMethod.Post,  BaseUrl + "/v1/oauth2/token");
        
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth64);
        
        request.Content = GrantTypeContent;
        
        var response = await Http.SendAsync(request);
        
        return await response.Content.ReadFromJsonAsync<PaypalAccessToken>();
    }

    public class PaypalAuthAssertion1
    {
        [JsonPropertyName("alg")]
        public string Alg { get; set; }
    }
    
    public class PaypalAuthAssertion2
    {
        [JsonPropertyName("iss")]
        public string Iss { get; set; }
        
        [JsonPropertyName("email")]
        public string PayerId { get; set; }
    }

    public static string GetAuthAssertion()
    {
        var auth1Obj = new PaypalAuthAssertion1()
        {
            Alg = "none"
        };

        var auth2Obj = new PaypalAuthAssertion2()
        {
            Iss = PaypalConfig.Current.ClientId,
            PayerId = PaypalConfig.Current.MerchantId
        };
        
        var auth1 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(auth1Obj)));
        var auth2 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(auth2Obj)));

        return $"{auth1.TrimEnd('=')}.{auth2.TrimEnd('=')}.";
    }
    
    [ValourRoute(HttpVerbs.Post, "api/orders/{productId}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> PostOrderAsync(UserService userService, string productId)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        
        var token = await GetAccessToken();
        PaypalPurchaseUnit unit = null;
        switch (productId)
        {
            case "VC500":
                unit = ValourOrderTypes.VC500;
                break;
            case "VC1000":
                unit = ValourOrderTypes.VC1000;
                break;
            case "VC2000":
                unit = ValourOrderTypes.VC2000;
                break;
        }
        
        if (unit is null)
            return ValourResult.BadRequest("Unknown product id " + unit);

        PaypalPurchasePayload payload = new()
        {
            Intent = "CAPTURE",
            PurchaseUnits = new List<PaypalPurchaseUnit>
            {
                unit
            },
            ReferenceId = $"{userId}-{productId}-{DateTime.UtcNow}",
            SoftDescriptor = $"VALOUR - {productId}",
            AppContext = new AppContext()
            {
                ShippingPreference = "NO_SHIPPING"
            }
        };
        
        
        var request = new HttpRequestMessage(HttpMethod.Post,  BaseUrl + $"/v2/checkout/orders");
        request.Content = JsonContent.Create(payload);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        
        var response = await Http.SendAsync(request);
        
        var stringContent = await response.Content.ReadAsStringAsync();
        
        if (!response.IsSuccessStatusCode)
        {
            return ValourResult.Problem("An unexpected error occurred.");
        }
        
        var idObj = JsonSerializer.Deserialize<IdObject>(stringContent);
        
        OrderIdsToType.Add(idObj.Id, productId);
        
        return ValourResult.RawJson(stringContent);
    }

    [ValourRoute(HttpVerbs.Post, "api/orders/{orderId}/capture")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> CaptureOrderAsync(UserService userService, EcoService ecoService, string orderId)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        
        var token = await GetAccessToken();
        var request = new HttpRequestMessage(HttpMethod.Post,  BaseUrl + $"/v2/checkout/orders/{orderId}/capture");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Content = new StringContent("");
        request.Content.Headers.ContentType.MediaType = "application/json";
        
        var response = await Http.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            return ValourResult.Problem("An unexpected error occurred. " + responseContent);
        }
        
        var stringContent = await response.Content.ReadAsStringAsync();
        var idObj = JsonSerializer.Deserialize<CaptureResponse>(stringContent);

        var captureId = idObj.PurchaseUnits[0].Payments.Captures[0].Id;

        try
        {
            var accountTo = await ecoService.GetGlobalAccountAsync(userId);
            if (accountTo is null)
                throw new Exception("User Global account could not be found!");

            var amount = 0m;
            
            var productId = OrderIdsToType[orderId];
            switch (productId)
            {
                case "VC500":
                    amount = 500;
                    break;
                case "VC1000":
                    amount = 1000;
                    break;
                case "VC2000":
                    amount = 2000;
                    break;
            }
            
            if (amount == 0)
                throw new Exception("Unknown product id " + productId);
            
            var transaction = new Transaction()
            {
                Id = Guid.NewGuid().ToString(),
                PlanetId = ISharedPlanet.ValourCentralId,
                UserFromId = ISharedUser.VictorUserId,
                AccountFromId = 21365328233627648,
                UserToId = userId,
                AccountToId = accountTo.Id,
                TimeStamp = DateTime.UtcNow,
                Description = "Online Purchase - Thank you!",
                Amount = amount,
                Fingerprint = Guid.NewGuid().ToString(),
            };
            
            var transactionResult = await ecoService.ProcessTransactionAsync(transaction);

            if (!transactionResult.Success)
                throw new Exception(transactionResult.Message);
        }
        catch (Exception ex)
        {
            try
            {
                Console.WriteLine("Critical transaction error: " + ex.Message);

                var refundToken = await GetAccessToken();

                // ROLL BACK TRANSACTION
                var refundRequest = new HttpRequestMessage(HttpMethod.Post,
                    BaseUrl + $"/v2/payments/captures/{captureId}/refund");
                refundRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refundToken.AccessToken);
                var assertion = GetAuthAssertion();
                refundRequest.Headers.Add("PayPal-Auth-Assertion", assertion);
                refundRequest.Content = new StringContent("{}");
                refundRequest.Content.Headers.ContentType.MediaType = "application/json";

                var refundResponse = await Http.SendAsync(refundRequest);

                if (!refundResponse.IsSuccessStatusCode)
                {
                    var responseContent = await refundResponse.Content.ReadAsStringAsync();
                    Console.WriteLine("FAILED REFUND! " + responseContent);
                    
                    Console.WriteLine("CRITICAL REFUND EXCEPTION! " + ex.Message);
                    throw new Exception("Critical error in refund!");
                }
                else
                {
                    return ValourResult.Problem(
                        "An error occurred in funding your account. Your purchase has been refunded. Please try again.");
                }
            }
            catch (Exception)
            {
                return ValourResult.Problem(
                    "There was a critical error, and paypal failed to refund your purchase. Please contact support at support@valour.gg. We apologize for this inconvenience.");
            }
        }

        return ValourResult.Ok("Success! Transaction id: " + captureId);
    }

}