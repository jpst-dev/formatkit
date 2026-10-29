# Task 6 report: personal data checks

Implemented UserFoldersCheck (data.user-folders), OtherUsersCheck (data.other-users), VolumesCheck (data.volumes) plus internal FolderScan helper (skips RecallOnDataAccess/Offline, counts unreadable folders), 28 resource strings in both resx files (singular/plural keys for unreadable folders).

## TDD
RED: `dotnet test tests/FormatKit.Prepare.Tests` -> CS0234 "FormatKit.Prepare.Checks.Data" does not exist in all three new test files (types not yet written).
GREEN: `dotnet test -- --filter-not-trait "Category=Integration"` -> 1031 passed, 0 failed, no warnings.

## Files
src/FormatKit.Prepare/Checks/Data/{FolderScan,UserFoldersCheck,OtherUsersCheck,VolumesCheck}.cs; Resources/Strings.resx, Strings.pt-BR.resx; tests/FormatKit.Prepare.Tests/Checks/Data/{UserFolders,OtherUsers,Volumes}CheckTests.cs

## Decisions
- One item per known folder (6 items) from user-folders. Folders not on a lost path: Ok. Empty/only placeholders: Ok.
- Volumes: lost-VHDX volumes excluded; any null disk number (including Windows volume) -> one NotChecked; no other volumes -> no item.
- Other users: profile folder name used as account name; size omitted when any folder unreadable.
