using Microsoft.VisualStudio.TestTools.UnitTesting;

// 測試彼此獨立（無共享狀態），允許 method 層級平行執行。
[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]
