using System.Security.Cryptography;
using System.Text;

namespace KarakusAluminyum.Web.Data;

// İçerik yenilemesi 1'deki varsayılan metinlerin parmak izleri. Eşleşen metne yönetici dokunmamıştır ve yenisiyle değiştirilebilir.
internal static class ContentFingerprints
{
    private static readonly HashSet<string> Version1 = new()
    {
        "007b8ca533c830c3", "05520c58899af9d6", "0587ae7130904617", "06a2f5dae15e4eef", "0ce845154e5a011e",
        "0d990bff0b3b9a42", "10d150dfa8258731", "199bf55c827c5f09", "1ad281359eee3990", "2a36bbfee98b8a37",
        "2d0f574a09af2297", "2d496409a9ff554f", "2e4c3d0b463a7ba1", "2fbfcb2d9899dfd9", "335f10338d0b22e2",
        "3bf251f7b207259c", "4077bf27023da7c2", "486aa79a158bef02", "4efbe523cb33e2d2", "557f65d7ab7b0ed4",
        "55db6ab356610c81", "597fdbe1d6eb8cae", "59e407c3faedacb5", "59e86cfe84d5b424", "5c28879dcc0608ed",
        "5d3cd405419fd65c", "5f41db56836a3166", "6178d71a1c14d71d", "64ba246209d6b032", "658d3809050ce0be",
        "66de75202ed890c0", "68a0ae4a209e7f1f", "6928a6e1f9b8db62", "6b4fc9c2919b0443", "6c7f15402645c9c1",
        "6fa0bf6b579a811a", "726287768d32ee6f", "7682c13623e86bb5", "798482b9f28266af", "7e32ab8c5ed2ce4b",
        "82914740e25a3492", "832d77fa2db382e0", "849dee5837e12353", "8961ee5bc706a492", "8c61d9869c21b775",
        "8d108ad7f0ed09b0", "91c286da53f8764f", "9454f625867d8ba6", "a29c573ecac5030b", "a4c333b6c943cbad",
        "a4fcec04cf826ec7", "a705804988b60743", "a9315893539da381", "a9f1a49884c91f83", "aa3891dd23cdeff6",
        "ad2ac58c0f4b5a5c", "af876fe60ca59a56", "b12993036ae2af1a", "b3c0988624d265b4", "b3f9f70cc783f5c0",
        "b47c9a7e73f8b9d3", "c1a8c47288334972", "c1d2f629016d03e7", "c1e13b45054a4d46", "c8d839dc7c12232c",
        "c95c1a20c9ee0c81", "cb551df5a1e64a93", "cbf546f0d04ac3d3", "ce05de4c1cb7c126", "cfe64162827b5a4f",
        "d3d84e7c7c86f650", "d46ef960a58bf22e", "d6b8200f3b0f2001", "d73e1a0ce1102e2e", "d8e4f974308fe465",
        "d8e89658a93e529e", "d95e9370faf1d3ee", "d99bea6e24219e09", "da8508cac019f322", "dcb2370145e8bb96",
        "dcfdfe5012637553", "e01bca02632e41be", "e2acc39b70e75e9e", "eaf7fad2b3585ba7", "ecf289229b5f412e",
        "edd5758f13e5e942", "f6978b6fcead440a", "f7fe634060088561", "fdd79280be358c13"
    };

    public static bool IsVersion1Default(string? value)
        => value is not null && Version1.Contains(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16]);
}
