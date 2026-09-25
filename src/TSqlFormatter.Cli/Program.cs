using System.Text;
using TSqlFormatter.Cli;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
return await SqlFormatterCli.RunAsync(args, Console.In, Console.Out, Console.Error);
