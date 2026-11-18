using System.Net;
using System.Net.Http.Json;
using Chat.Client.Services;
using Xunit;
namespace Chat.Client.Tests;
public class PendingTransferClientTests
{
 [Fact] public async Task LoadsPendingTransfersThroughContractEndpoint()
 {
  var group=Guid.NewGuid();var request=Guid.NewGuid();var user=Guid.NewGuid();
  using var http=new HttpClient(new Handler(message=>{
   Assert.Equal(HttpMethod.Get,message.Method);
   Assert.Equal($"/api/conversations/{group}/admin-transfer",message.RequestUri!.AbsolutePath);
   Assert.False(message.Headers.Contains("X-CSRF-TOKEN"));
   return new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new[]{new{id=request,conversationId=group,toUserId=user,status="Pending"}})};
  })){BaseAddress=new Uri("https://localhost/")};
  var api=new GroupApiClient(new ApiRequestSender(http));
  var item=Assert.Single(await api.GetPendingAdminTransfersAsync(group));
  Assert.Equal(request,item.Id);Assert.Equal(user,item.ToUserId);
 }
 sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(send(request));
 }
}
