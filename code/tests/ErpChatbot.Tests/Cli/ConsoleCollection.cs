using Xunit;

namespace ErpChatbot.Tests.Cli;

// Console.SetOut là global state, nên các test đọc Console.Out không được chạy song song.
[CollectionDefinition("Console", DisableParallelization = true)]
public class ConsoleCollection;
