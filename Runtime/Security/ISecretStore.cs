namespace Ecaty.Agents.Security
{
    public interface ISecretStore
    {
        byte[] Load();
        void Save(byte[] data);
    }
}
