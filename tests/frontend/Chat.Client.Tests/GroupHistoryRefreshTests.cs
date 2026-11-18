using Chat.Client.Models;
using Chat.Client.Services;
using Chat.Client.State;
using Xunit;
public class GroupHistoryRefreshTests
{
 [Fact] public async Task BlockingSenderRemovesAlreadyLoadedMessages()
 {
  var api=new Api();var session=new ChatSession(api,10);
  await session.SyncAsync(api.Id);api.Blocked=true;
  await session.RefreshVisibleAsync(api.Id);
  Assert.DoesNotContain(session.State.For(api.Id).Messages,m=>m.SenderUserId==api.Sender);
 }
 [Fact] public async Task UnblockingSenderRestoresOlderHiddenMessages()
 {
  var api=new Api{Blocked=true};var session=new ChatSession(api,10);
  await session.SyncAsync(api.Id);api.Blocked=false;
  await session.RefreshVisibleAsync(api.Id);
  Assert.Contains(session.State.For(api.Id).Messages,m=>m.Seq==1);
 }
 [Fact] public async Task RefreshPreservesLoadedWindowAndAllowsOlderPagingAfterUnblock()
 {
  var api=new Api{Count=50};var session=new ChatSession(api,10);
  await session.SyncAsync(api.Id);await session.LoadOlderAsync(api.Id);
  api.Blocked=true;await session.RefreshVisibleAsync(api.Id);
  Assert.All(session.State.For(api.Id).Messages,m=>Assert.Equal(0,m.Seq%2));
  api.Blocked=false;await session.RefreshVisibleAsync(api.Id);
  while(session.State.For(api.Id).HasOlder) await session.LoadOlderAsync(api.Id);
  Assert.Equal(Enumerable.Range(1,50).Select(i=>(long)i),session.State.For(api.Id).Messages.Select(m=>m.Seq));
 }
 [Theory]
 [InlineData(false)]
 [InlineData(true)]
 public async Task ReconnectWithTenMessagePageDrainsMissingMessagesWithoutDuplicates(bool group)
 {
  var api=new Api{Count=20};var session=new ChatSession(api,10);
  await session.SyncAsync(api.Id);api.Count=271;
  if(group) { await session.RefreshVisibleAsync(api.Id);await session.RefreshVisibleAsync(api.Id); }
  else { await session.SyncAsync(api.Id);await session.SyncAsync(api.Id); }
  Assert.Equal(Enumerable.Range(11,261).Select(i=>(long)i),session.State.For(api.Id).Messages.Select(m=>m.Seq));
 }
 class Api:IChatMessages
 {
  public Guid Id=Guid.NewGuid(),Sender=Guid.NewGuid();public bool Blocked;public int Count=3;
  List<MessageResponse> Visible()=>Enumerable.Range(1,Count).Where(n=>!Blocked||n%2==0).Select(n=>new MessageResponse(Guid.Parse($"00000000-0000-0000-0000-{n:D12}"),Id,n==2?Guid.Empty:Sender,n,"body",DateTimeOffset.UtcNow)).ToList();
  public Task<List<MessageResponse>> GetLatestAsync(Guid id)=>Task.FromResult(Visible().TakeLast(10).ToList());
  public Task<List<MessageResponse>> GetNewAsync(Guid id,long seq)=>Task.FromResult(Visible().Where(m=>m.Seq>seq).Take(100).ToList());
  public Task<List<MessageResponse>> GetOlderAsync(Guid id,long seq)=>Task.FromResult(Visible().Where(m=>m.Seq<seq).TakeLast(10).ToList());
  public Task<MessageResponse> SendAsync(Guid id,SendMessageRequest request)=>throw new NotImplementedException();
 }
}
