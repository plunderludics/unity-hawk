using UnityEngine;

namespace UnityHawk {

[CreateAssetMenu(menuName = "Plunderludics/UnityHawk/Config", fileName = "UnityHawkConfig")]
public class UnityHawkConfig: ScriptableObject {
	[Tooltip("the log output path, relative to the project folder")]
	[SerializeField] string m_LogsPath;
	public string LogsPath => m_LogsPath;

    [Tooltip("the firmware path, relative to streaming assets")]
	[SerializeField] string m_FirmwarePath;
	public string FirmwarePath => m_FirmwarePath;

    [Tooltip("if left blank, defaults to initial romFile directory")]
	[SerializeField] string m_SavestatesOutputPath;
	public string SavestatesOutputPath => m_SavestatesOutputPath;

    [Tooltip("if left blank, defaults to initial romFile directory")]
	[SerializeField] string m_RamWatchOutputPath;
	public string RamWatchOutputPath => m_RamWatchOutputPath;

}
}