namespace EndfieldAicWeb.Infrastructure.Icons;

/// <summary>
/// アイコンファイルの内容取得を抽象化する。
/// ローカル検証・ツールでは <see cref="FileSystemIconProvider"/> を使い、
/// Blazor WASM では wwwroot 配信物を取得する実装に差し替える。
/// </summary>
public interface IIconFileProvider
{
    /// <summary>
    /// マニフェスト相対パス（例: <c>icons/icon-ore.png</c>）の内容を返す。
    /// ファイルが存在しない・読み取れない場合は null。
    /// </summary>
    byte[]? ReadAllBytes(string path);
}
