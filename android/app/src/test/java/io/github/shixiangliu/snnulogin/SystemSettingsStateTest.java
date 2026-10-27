package io.github.shixiangliu.snnulogin;

import org.junit.Test;
import static org.junit.Assert.*;

public class SystemSettingsStateTest {
    @Test public void platformAllowlistIsNotPresentedAsTheSystemsOwnBackgroundSwitch() {
        SystemSettingsState.Entry state = SystemSettingsState.battery(false, false);
        assertEquals("系统默认管理", state.title); assertEquals(UiModel.Tone.NEUTRAL, state.tone);
        assertFalse(state.detail.contains("未取消"));
    }
    @Test public void openingAVendorScreenCannotBeDeclaredGranted() {
        assertFalse(SystemSettingsState.battery(false, false).ready);
        assertFalse(SystemSettingsState.battery(null, null).ready);
    }
    @Test public void actualAndroidExemptionStatesItsScope() {
        SystemSettingsState.Entry state = SystemSettingsState.battery(true, false);
        assertTrue(state.ready); assertEquals("已获系统省电豁免", state.title);
        assertTrue(state.detail.contains("厂商"));
        assertTrue(state.detail.contains("以系统页面为准"));
    }
    @Test public void actualBackgroundRestrictionTakesPriorityOverPowerAllowlist() {
        SystemSettingsState.Entry state = SystemSettingsState.battery(true, true);
        assertFalse(state.ready); assertEquals(UiModel.Tone.WARNING, state.tone);
        assertEquals("系统限制后台运行", state.title);
    }
    @Test public void unavailableReadIsNotClaimedToBeDeniedOrAllowed() {
        SystemSettingsState.Entry state = SystemSettingsState.battery(null, null);
        assertEquals("以系统设置为准", state.title); assertFalse(state.ready); assertEquals(UiModel.Tone.NEUTRAL, state.tone);
    }
    @Test public void noManufacturerIsSingledOutInUserFacingText() {
        for (SystemSettingsState.Entry entry : new SystemSettingsState.Entry[]{
                SystemSettingsState.battery(true, false), SystemSettingsState.battery(false, false),
                SystemSettingsState.battery(true, true), SystemSettingsState.battery(null, null)}) {
            assertFalse(entry.title.contains("ColorOS")); assertFalse(entry.detail.contains("ColorOS"));
        }
    }
}
