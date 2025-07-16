using AmongUs.GameOptions;
using System;

namespace TOHE.Modules;

public class NormalGameOptionsSender : GameOptionsSender
{
    private LogicOptions _logicOptions;
    public override IGameOptions BasedGameOptions
        => GameOptionsManager.Instance.CurrentGameOptions;

    public override bool IsDirty
    {
        get
        {
            try
            {
                if (_logicOptions == null || !GameManager.Instance.LogicComponents.Contains(_logicOptions))
                {
                    foreach (var glc in GameManager.Instance?.LogicComponents.GetFastEnumerator())
                        if (glc.TryCast<LogicOptions>(out var lo))
                            _logicOptions = lo;
                }
                return _logicOptions != null && _logicOptions.IsDirty;
            }
            catch (Exception error)
            {
                Logger.Warn($"_logicOptions == null {_logicOptions == null} --- GameManager.Instance.LogicComponents == null {GameManager.Instance.LogicComponents == null} - Error: {error}", "NormalGameOptionsSender.IsDirty.Get");
                return _logicOptions != null && _logicOptions.IsDirty;
            }
        }
        protected set
        {
            try
            {
                _logicOptions?.ClearDirtyFlag();
            }
            catch (Exception error)
            {
                Logger.Warn(error.ToString(), "NormalGameOptionsSender.IsDirty.ProtectedSet");
            }
        }
    }

    public override IGameOptions BuildGameOptions()
        => BasedGameOptions;
}