using BattleNusantara.Server.Platform;

// Maintenance: "backup <file>" and "restore <file>" act on the embedded database and exit.
if (args is ["backup" or "restore", var file, ..]) return await DatabaseTools.Run(args[0], file, args[2..]);

var app = ServerHost.Build(args);
await app.RunAsync();
return 0;
