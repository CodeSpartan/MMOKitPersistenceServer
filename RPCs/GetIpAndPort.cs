using Microsoft.AspNetCore.Hosting.Server;
using Newtonsoft.Json.Linq;
using System.Net;

namespace PersistenceServer.RPCs
{
    public class GetIpAndPort : BaseRpc
    {
        public GetIpAndPort()
        {
            RpcType = RpcType.RpcGetIpAndPort; // set it to the RpcType you want to catch
        }

        // Read message from the reader, then enqueue an Action on the concurrent queue server.Processor.ConQ
        // For example: Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("like this"));
        // Look at other RPCs for more examples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            var charId = reader.ReadInt32();
            var cookie = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(async () => await ProcessGetIpAndPort(charId, cookie, connection));
        }

        private async Task ProcessGetIpAndPort(int charId, string cookie, UserConnection connection)
        {
            var accountId = Server!.GameLogic.GetAccountIdByCookie(cookie);
            if (accountId < 0)
            {
                Console.WriteLine("GetIpAndPort failed: user provided bad cookie!");
                Console.WriteLine("Cookie sent by client: " + cookie);
                // send something to disconnect the player to the main menu
                connection.Send(MergeByteArrays(
                    ToBytes(RpcType.RpcGetIpAndPort),
                    ToBytes(false), // means it's a failure
                    WriteMmoString("")
                ));
                return;
            }

            // Get the last zone the character was saved to and find a server instance with that zone
            var charInfo = await Server!.Database.GetCharacter(charId, accountId);
            if (charInfo == null)
            {
                Console.WriteLine("GetIpAndPort failed: user provided bad character id!");
                // send something to disconnect the player to the main menu
                connection.Send(MergeByteArrays(
                    ToBytes(RpcType.RpcGetIpAndPort),
                    ToBytes(false), // means it's a failure
                    WriteMmoString("")
                ));
                return;
            }

            string zone = "";
            JObject jsonObject = JObject.Parse(charInfo.SerializedCharacter);

            // If it's a newly created character, it'll have a NewCharacter field of type bool and its value will be true
            if (jsonObject.TryGetValue("NewCharacter", out JToken? value) && value.Type == JTokenType.Boolean && value.ToObject<bool>() == true)
            {
                //@TODO: Define what zone to connect to if it's a newly created character.
                //@TODO: Is there a zone specific for newly created characters? Is it always the same or does it depend on species/origin/etc?
                //for now, just do nothing, leave the zone empty
            }

            // If it's not a newly created character, but a previously saved one, we can retreive the character's "zone" field from the character's json
            if (jsonObject.TryGetValue("Zone", out JToken? zoneValue) && zoneValue.Type == JTokenType.String)
            {
                zone = zoneValue.ToString();
            }

            // Depending on the zone, we may want the player to connect to a certain IP and port
            // If a server with this zone doesn't exist or if it exists but is full, we need to launch a new instance
            var serverInstance = await Server!.GameLogic.GetOrStartServerForZone(zone);

#if DEBUG
            Console.WriteLine($"Returning game server IP & Port (127.0.0.1:{serverInstance.Port} -- due to debug config) for character id: {charId}");
            byte[] msg = MergeByteArrays(
                ToBytes(RpcType.RpcGetIpAndPort), 
                ToBytes(true), // means it's a success
                WriteMmoString($"127.0.0.1:{serverInstance.Port}")
                );
            connection.Send(msg);
#else
            Console.WriteLine($"Returning game server IP & Port ({serverInstance.Conn.Ip}:{serverInstance.Port}) for character id: {charId}");
            byte[] msg = MergeByteArrays(
                ToBytes(RpcType.RpcGetIpAndPort), 
                ToBytes(true), // means it's a success
                WriteMmoString($"{serverInstance.Conn.Ip}:{serverInstance.Port}")
                );
            connection.Send(msg);
#endif
        }
    }
}