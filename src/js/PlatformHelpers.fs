namespace Fable.Giraffe

[<AutoOpen>]
module PlatformHelpers =
    /// Length of a sequence/byte array. On JS this is a plain number; no
    /// boxed-int wrapper to unwrap (unlike the Python/BEAM backends).
    let len (x: 'T) : int = Seq.length x

    /// On JS there is no separate boxed-int type, so this is the identity.
    let inline toNativeInt (x: int) : int = x
