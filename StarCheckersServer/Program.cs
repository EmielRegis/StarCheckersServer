namespace StarCheckersServer
{
    class Program
    {
        static void Main(string[] args)
        {
            new AsyncServer().StartListening();
        }
    }
}
