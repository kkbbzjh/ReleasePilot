using ReleasePilot.Services;
var cases = new (string Local,string Remote,string Expected)[] {
("0.2.0+7f0d62a83b4d41b5bd0fab6ef29fc1a26954e61c","v0.2.0","已是最新"),
("0.2.0.0","v0.2.0","已是最新"),
("0.2","v0.2.0","已是最新"),
(" V0.2.0 "," v0.2.0 ","已是最新"),
("0.2.0+old","0.2.0+new","已是最新"),
("0.2.0","v0.3.0","发现更新"),
("0.3.0","v0.2.0","本地版本较新"),
("0.2.0.1","v0.2.0","本地版本较新"),
("0.2.0-rc.1+abc","v0.2.0","发现更新"),
("0.2.0","v0.2.0-rc.1","本地版本较新"),
("0.2.0-rc.2","v0.2.0-rc.10","发现更新"),
("0.2.0-alpha","v0.2.0-beta","发现更新"),
("0.2.0-alpha.1","v0.2.0-alpha","本地版本较新"),
("0.2.0-1","v0.2.0-alpha","发现更新"),
("0.2.0-rc.01","v0.2.0","需要确认"),
("0.2.0+","v0.2.0","需要确认"),
("0.2.0","nightly-20261021","需要确认"),
("0.2.0 (abcdef)","v0.2.0","需要确认"),
("9999999999999.2.0","v0.2.0","需要确认"),
("","v0.2.0","未关联本地版本"),
("0.2.0","","需要确认"),
};
foreach(var (local,remote,expected) in cases) {
 var result=VersionHelper.Compare(local,remote);
 if(result!=expected)throw new Exception($"{local} vs {remote}: expected {expected}, got {result}");
}
Console.WriteLine($"PASS {cases.Length} version comparisons");
if(args.Length==2)
{
    var actual=VersionHelper.ReadExeVersion(args[0]);
    Console.WriteLine($"Published EXE ProductVersion: {actual}");
    if(VersionHelper.Compare(actual,args[1])!="已是最新")
        throw new Exception($"Published EXE version {actual} does not match {args[1]}");
    Console.WriteLine("PASS published EXE version matches release tag");
}
