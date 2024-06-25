
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    public interface IBrokerClient
    {
        void Connect(bool altBroker = false);

        void Publish(byte[] payload);

        void PublishSingle(byte[] payload, string variabletopic);

        void PublishMetadata(byte[] metadata);
    }
}