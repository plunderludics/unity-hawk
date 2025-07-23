using UnityEngine;
using UnityEngine.Serialization;

namespace UnityHawk {

[CreateAssetMenu(menuName = "Plunderludics/UnityHawk/Config", fileName = "UnityHawkConfig")]
public class UnityHawkConfig: ScriptableObject {
	[Tooltip("the log output path, relative to the project folder")]
	[SerializeField] string bizHawkLogsPath = "Logs/BizHawk";
	public string BizHawkLogsPath => bizHawkLogsPath;

    [Tooltip("the firmware path, relative to streaming assets")]
	[SerializeField] string firmwarePath = "Firmware";
	public string FirmwarePath => firmwarePath;

    [Tooltip("if left blank, defaults to initial romFile directory")]
	[SerializeField] string savestatesOutputPath = "";
	public string SavestatesOutputPath => savestatesOutputPath;

	[Tooltip("if left blank, defaults to initial romFile directory")]
	[SerializeField] string ramWatchOutputPath = "";
	public string RamWatchOutputPath => ramWatchOutputPath;

}
}