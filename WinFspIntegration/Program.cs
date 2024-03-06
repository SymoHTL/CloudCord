using Fsp;

var myFileSystem = new WinFspService("http://localhost:5000");
var host = new FileSystemHost(myFileSystem);
host.Mount("X:");

Console.ReadKey();
host.Unmount();