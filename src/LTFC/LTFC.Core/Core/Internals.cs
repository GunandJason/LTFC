using System.Runtime.CompilerServices;

// 允许主程序与自测工程访问内部类型（仅用于本套件内部，不对外发布 API）
[assembly: InternalsVisibleTo("LTFC")]
[assembly: InternalsVisibleTo("LTFC.SelfTest")]
[assembly: InternalsVisibleTo("ClassTell")]
[assembly: InternalsVisibleTo("ClassTell.SelfTest")]
[assembly: InternalsVisibleTo("ClassTell.Lib")]
