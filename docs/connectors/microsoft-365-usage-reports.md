# Microsoft 365 usage report profiles 1.0.0

Three profiles interpret the Microsoft 365 administrative usage reports as
consultant-uploaded CSV files. They are `SyntheticValidated`: repository tests ran
against synthetic files built from Microsoft's documented schemas; no customer
export or live tenant has certified them. A report file is import evidence, not a
verified Microsoft Graph connection, even when a consultant downloaded it with
Graph. Framework behavior, reuse and acceptance tests are in
[import-profiles.md](../import-profiles.md).

| Profile | Object / key | Section | Relation |
| --- | --- | --- | --- |
| `m365.mailboxUsageDetail` 1.0.0 | `mailbox` in `m365report.upn` (lower-case UPN, provisional) | `exchange.mailboxUsage` | `mailboxOf` mailbox → `user`/`upn` |
| `m365.oneDriveUsageAccountDetail` 1.0.0 | `oneDrive` in `sharepoint.siteId` when the row supplies a valid `Site Id`, otherwise `m365report.siteUrl` (normalized URL, provisional) when the column or cell is empty; alternate `url` | `onedrive.accountUsage` | `ownerOf` `user`/`upn` → OneDrive |
| `m365.sharePointSiteUsageDetail` 1.0.0 | `site` in `sharepoint.siteId` (required `Site Id`); alternate `url` | `sharepoint.siteUsage` | `ownerOf` `user`/`upn` → site |

## Acquiring the reports

Use the Microsoft Graph v1.0 report functions in the public `graph.microsoft.com`
cloud. Each returns `302 Found` to a short-lived, pre-authenticated CSV URL; save
that file unchanged.

| Report | Endpoint | Microsoft Graph PowerShell |
| --- | --- | --- |
| Mailbox usage detail | `GET /reports/getMailboxUsageDetail(period='D7')` | `Get-MgReportMailboxUsageDetail -Period D7 -OutFile mailbox-usage.csv` |
| OneDrive usage account detail | `GET /reports/getOneDriveUsageAccountDetail(period='D7')` or `(date=YYYY-MM-DD)` | `Get-MgReportOneDriveUsageAccountDetail -Period D7 -OutFile onedrive-usage.csv` |
| SharePoint site usage detail | `GET /reports/getSharePointSiteUsageDetail(period='D7')` or `(date=YYYY-MM-DD)` | `Get-MgReportSharePointSiteUsageDetail -Period D7 -OutFile sharepoint-site-usage.csv` |

Periods are `D7`, `D30`, `D90` and `D180`. The cmdlets are in module
`Microsoft.Graph.Reports`; connect with `Connect-MgGraph -Scopes Reports.Read.All`.

Rights: Microsoft Graph permission `Reports.Read.All` (delegated or application).
Delegated callers additionally need one of Global Administrator, Exchange
Administrator, SharePoint Administrator, Skype for Business (Lync) Administrator,
Teams Service Administrator, Teams Communications Administrator or Reports Reader.
Global Reader and Usage Summary Reports Reader see tenant summaries only and cannot
download these user-level detail reports. Clarity itself performs no Graph report
download in this slice and stores no report credential.

Sources: [getMailboxUsageDetail](https://learn.microsoft.com/graph/api/reportroot-getmailboxusagedetail?view=graph-rest-1.0),
[getOneDriveUsageAccountDetail](https://learn.microsoft.com/graph/api/reportroot-getonedriveusageaccountdetail?view=graph-rest-1.0),
[getSharePointSiteUsageDetail](https://learn.microsoft.com/graph/api/reportroot-getsharepointsiteusagedetail?view=graph-rest-1.0),
[concealed user names](https://learn.microsoft.com/microsoft-365/troubleshoot/miscellaneous/reports-show-anonymous-user-name).

## Accepted format

UTF-8 CSV with or without BOM, comma separator, RFC 4180 quoting (quoted commas,
doubled quotes and embedded line breaks), CRLF or LF, header on line 1. Headers are
the exact English Graph headers, matched case-insensitively in any order. Dates are
`yyyy-MM-dd`; booleans `True`/`False`; sizes are integer **bytes**; counts are
integer items/files/views/pages; `Report Period` is integer days.

Mailbox required columns: `Report Refresh Date`, `User Principal Name`,
`Display Name`, `Is Deleted`, `Deleted Date`, `Created Date`, `Last Activity Date`,
`Item Count`, `Storage Used (Byte)`, `Issue Warning Quota (Byte)`,
`Prohibit Send Quota (Byte)`, `Prohibit Send/Receive Quota (Byte)`,
`Deleted Item Count`, `Deleted Item Size (Byte)`, `Report Period`; optional
`Deleted Item Quota (Byte)` and `Has Archive`.

OneDrive required columns: `Report Refresh Date`, `Site URL`, `Owner Display Name`,
`Is Deleted`, `Last Activity Date`, `File Count`, `Active File Count`,
`Storage Used (Byte)`, `Storage Allocated (Byte)`, `Owner Principal Name`,
`Report Period`; optional `Site Id`.

OneDrive identity selection is per row: populated and blank `Site Id` cells may
coexist. A blank or whitespace-only cell uses that row's usable `Site URL`; an
invalid nonblank ID rejects the row rather than silently falling back. A concealed
URL cannot provide a provisional identity. The preview lists every identity
column/namespace used, and owner relationships keep the selected row's namespace.

SharePoint required columns: `Report Refresh Date`, `Site Id`, `Site URL`,
`Owner Display Name`, `Is Deleted`, `Last Activity Date`, `File Count`,
`Active File Count`, `Page View Count`, `Visited Page Count`, `Storage Used (Byte)`,
`Storage Allocated (Byte)`, `Root Web Template`, `Owner Principal Name`,
`Report Period`.

Other columns Microsoft adds later are listed as unrecognized and ignored; they are
never converted. Synthetic examples are in `catalog/report-profiles/examples`.

## Unsupported variants

- Microsoft 365 admin center exports in megabytes (`Storage Used (MB)` and similar)
  are rounded; they are refused with `ReportUnsupportedVariant` (`MegabyteUnits`).
- Localized headers, semicolon delimiters, UTF-16 files, Excel re-saves that change
  date or number formats, and non-numeric quotas such as `Unlimited` are not
  supported; the file or row is refused rather than reinterpreted.
- Multi-geo files with more than one SharePoint root host, files mixing refresh
  dates or periods, and sovereign clouds are not supported.
- Quest and other third-party exports have no profile until a verified sample exists.

## Confidentiality and concealed identities

Microsoft conceals user, group and site names in reports by default since
1 September 2021 unless an administrator disables it in the admin center (or the
`adminReportSettings` API). Concealed values are 32 hexadecimal characters. Clarity
never reverses, matches or stores them as identifiers: rows with a concealed key are
counted as concealed and skipped; concealed display names, owners or URLs are
omitted from otherwise usable rows and counted. A concealed-only file fails with
`AllIdentifiersConcealed` and cannot be confirmed. Ask the tenant administrator for
a report exported without concealment when identities are needed. Raw files are
retained in the private Blob container and downloadable only by authorized dossier
members.

## Completeness limits

- A report is a snapshot of what Microsoft included; it is never a tenant-wide
  exhaustive inventory, so missing rows never prove deletion or absence.
- Usage data typically lags 24 to 48 hours; the observation date is the report
  refresh date. Deleted users leave usage reports after about 30 days.
- `Is Deleted` is recorded as a field, not as a presence change.
- SharePoint URLs can be blank under Customer Key or Customer Lockbox; blank values
  stay unknown. Group-owned sites often have no owner principal and stay unlinked.
- Reports carry no Exchange GUID, recipient type, proxy addresses, permissions,
  sharing links, membership or sensitivity labels.
- The reports do not name their tenant. The consultant enters and attests the tenant
  of the selected environment; Clarity records it as `consultantSupplied` and blocks
  files whose UPN domains or SharePoint root contradict evidence already collected
  for that environment.

## Tested versions

Profiles 1.0.0 with adapter 1.0.0, against the Graph v1.0 column lists published on
Microsoft Learn and checked when this slice was written. Synthetic tests cover each schema, BOM
and quoted fields, reordered headers, mixed populated/blank OneDrive IDs, blank versus zero versus invalid values,
concealed rows, missing columns and the megabyte variant. A pilot must download real
reports for each period and confirm their headers before claiming live readiness.
