
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    public interface IMessagePublisher
    {
        bool SendMessage(byte[] message);

        bool SendSingleMessage(byte[] message, bool batch, string variabletopic);

        bool SendMetadata(byte[] metadata);
    }
}
