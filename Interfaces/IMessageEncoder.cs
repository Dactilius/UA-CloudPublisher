
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using Opc.Ua.Cloud.Publisher.Models;

    public interface IMessageEncoder
    {
        string EncodeHeader(ulong messageID, bool isMetadata = false);

        string EncodePayload(MessageProcessorModel messageData, out ushort hash);
        string EncodeSinglePayload(MessageProcessorModel messageData, out ushort hash, out string variabletopic);

        string EncodeMetadata(MessageProcessorModel messageData);

        string EncodeStatus(ulong messageID);
    }
}