// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The two scan profiles the configuration fixes (IQ-23). The profile selects
/// the file classes a scan visits and the rules that run; an explicit
/// <c>scan.history</c> or <c>scan.maxFileBytes</c> always wins over the
/// profile's default for that key.
/// </summary>
public enum ScanProfile
{
    /// <summary>
    /// The default profile: ordinary files only, history off, and the
    /// 1,048,576-byte file limit. It is the scan the continuous-integration
    /// budget is measured on.
    /// </summary>
    Default = 0,

    /// <summary>
    /// The deep profile: data files and staging configuration on top of
    /// ordinary files, history on, and the 10,485,760-byte file limit.
    /// </summary>
    Deep = 1,
}
