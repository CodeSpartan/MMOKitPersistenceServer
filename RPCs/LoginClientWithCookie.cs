using System;
using System.Numerics;

namespace PersistenceServer.RPCs
{

    /// Happens when a player connects to the world map and already has a cookie ready from previous <see cref="LoginPassword"/> or <see cref="LoginWithSteam"/>   
    public class LoginClientWithCookie : BaseRpc
    {
        public LoginClientWithCookie()
        {
            RpcType = RpcType.RpcLoginClientWithCookie; // set it to the RpcType you want to catch
        }

        // Read message from the reader, then enqueue an Action on the concurrent queue server.Processor.ConQ
        // For example: Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("like this"));
        // Look at other RPCs for more examples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            var cookie = reader.ReadMmoString();
            var charId = reader.ReadInt32();
            if (charId < 0)
            {
                Console.WriteLine("LoginClientWithCookie: Incorrect char id, must be >= 0. Have you launched PIE in incorrect net mode?");
                return;
            }
#if DEBUG
            if (Server!.Settings.UniversalCookie == cookie)
            {
                Server!.Processor.ConQ.Enqueue(async () => await ProcessLoginClientFromEditor(charId, connection));
            }
            else 
            {
                Server!.Processor.ConQ.Enqueue(async () => await ProcessLoginClientWithCookie(cookie, charId, connection));
            }
#else
            Server!.Processor.ConQ.Enqueue(async () => await ProcessLoginClientWithCookie(cookie, charId, connection));
#endif
        }

        private async Task ProcessLoginClientWithCookie(string cookie, int charId, UserConnection connection)
        {
            var accountId = Server!.GameLogic.GetAccountIdByCookie(cookie);
            if (accountId < 0)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} LoginWithCookie failed for client: bad cookie");
                _ = connection.Disconnect(); // not awaited
                return;
            }
            
            var charInfo = await Server!.Database.GetCharacter(charId, accountId);
            if (charInfo != null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Client (IP: {connection.Ip}) relogged with character: {charInfo.Name}");
                ProcessLogin(charInfo, connection);
            }
            else
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} LoginWithCookie failed for client: bad char id");
                _ = connection.Disconnect(); // not awaited
            }
        }

        // If we're in Debug config, we assume the client connects from PIE and therefore doesn't have a valid cookie
        // Neither does it have a valid charId. It'll provide Pie Window ID instead, couting from 0.
        private async Task ProcessLoginClientFromEditor(int pieWindowId, UserConnection connection)
        {
            var charInfo = await Server!.Database.GetCharacterForPieWindow(pieWindowId);
            if (charInfo == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} LoginWithCookie failed for client: not enough characters in DB for PIE window: {pieWindowId}");
                _ = connection.Disconnect(); // not awaited
                return;
            } 
            else
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} LoginWithCookie: {charInfo.Name} logged in for PIE window {pieWindowId}");
            }
            ProcessLogin(charInfo, connection);
        }

        private void ProcessLogin(DatabaseCharacterInfo charInfo, UserConnection connection)
        {
            var player = Server!.GameLogic.UserReconnected(connection, charInfo);
            // if this char is in a guild
            // 1. send him the full guild roster
            // 2. tell all servers what this guy's guild is
            // 3. send a message to other online guild members that this one just came online
            if (charInfo.Guild != null)
            {
                var guild = Server!.GameLogic.GetPlayerGuild(connection);
                if (guild != null)
                {
                    // 1. send him the full guild roster
                    byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcGuildAllMembersUpdate), WriteMmoString(guild.GetGuildMembersJson()));
                    connection.Send(msg);

                    // 2. send message to all servers that a character with a certain id belongs to a guild with a certain id & name
                    // For server, params are: char id, guild name, guild id, guild rank
                    byte[] msgToServers = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(charInfo.CharId), WriteMmoString(guild.Name), ToBytes(guild.Id), ToBytes((int)charInfo.GuildRank!));
                    foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
                    {
                        serverConn.Send(msgToServers);
                    }

                    // 3. send a message to other online guild members that this one just came online
                    // for client, params are: char id, guild rank, online status (bool)
                    byte[] msgToGuildies = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(charInfo.CharId), ToBytes((int)charInfo.GuildRank!), ToBytes(true)); // true for online
                    foreach (var onlineMember in guild.GetOnlineMembers())
                    {
                        // if this is our character, we don't need to tell him that he just went online
                        if (onlineMember.Conn == connection) continue;
                        onlineMember.Conn.Send(msgToGuildies);
                    }
                }
            }
            // if this char is in a party, restore his party ref and send full party info to all online party members
            string? partyId = Server!.GameLogic.GetStoredDisconnectedPlayerPartyId(charInfo.CharId);
            if (partyId != null)
            {
                var partyRef = Server!.GameLogic.GetPartyById(partyId);
                if (partyRef != null)
                {
                    Server!.GameLogic.UnstoreDisconnectedPlayerPartyId(charInfo.CharId);
                    player.PartyRef = partyRef;
                    partyRef.SendFullPartyToInvolvedServers();
                }
            }
        }
    }
}