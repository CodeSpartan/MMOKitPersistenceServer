namespace PersistenceServer.RPCs
{
    public class LoginPassword : BaseRpc
    {
        public LoginPassword()
        {
            RpcType = RpcType.RpcLoginPassword; // set it to the RpcType you want to catch
        }

        // Read message from the reader, then enqueue an Action on the concurrent queue server.Processor.ConQ
        // For example: Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("like this"));
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string accountName = reader.ReadMmoString();
            string password = reader.ReadMmoString();
#if DEBUG
            Console.WriteLine($"(thread {Environment.CurrentManagedThreadId}): Login with password");
#endif            
            Server!.Processor.ConQ.Enqueue(async () => await ProcessLogin(accountName, password, connection));
        }

        private async Task ProcessLogin(string accountName, string password, UserConnection connection)
        {
            int accountId = await Server!.Database.LoginUser(accountName, password); // returns -1 if login failed
            // if account exists
            if (accountId >= 0)
            {                
                var cookie = BCrypt.Net.BCrypt.GenerateSalt();
                Console.WriteLine($"Logging in: '{accountName}', id: {accountId}, cookie: {cookie}");
                Server!.GameLogic.UserLoggedIn(accountId, cookie, connection);

                // sending true to signify "success", plus a cookie
                // the cookie will allow a reconnection later, when the user changes the level to enter the game server
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginPassword), ToBytes(true), WriteMmoString(cookie));                
                connection.Send(msg);
            }
            // if account doesn't exist or is banned
            else
            {
                Console.WriteLine($"Logging in for '{accountName}' failed: bad credentials");
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginPassword), ToBytes(false)); // sending false to signify "failure"
                connection.Send(msg);
            }
        }
    }
}