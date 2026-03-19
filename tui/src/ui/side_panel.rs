// SidePanel container widget (§8.3 of specs/tui-client.md).
// Phase 3: passes Theme and Strings through to PlanPanel and SubAgentTable.

use crate::{
    app::state::AppState,
    i18n::Strings,
    theme::Theme,
    ui::{plan_panel::PlanPanel, subagent_table::SubAgentTable},
};
use ratatui::{buffer::Buffer, layout::Rect, widgets::Widget};

pub struct SidePanel<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> SidePanel<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self { state, theme, strings }
    }

    pub fn should_show(state: &AppState) -> bool {
        state.plan.is_some() || !state.subagent_entries.is_empty()
    }
}

impl Widget for SidePanel<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        // Prefer SubAgentTable when active SubAgents are present.
        if !self.state.subagent_entries.is_empty() {
            SubAgentTable::new(self.state, self.theme, self.strings).render(area, buf);
        } else if self.state.plan.is_some() {
            PlanPanel::new(self.state, self.theme, self.strings).render(area, buf);
        }
    }
}
