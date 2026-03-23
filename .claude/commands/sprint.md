# /sprint

Show current sprint status. What's done, what's next, what's blocked.

1. Read `docs/sprint1.md`
2. Run `git log main..HEAD --oneline` to see what's been committed
3. Cross-reference commits against the sprint checklist
4. Output:

**✅ Done (committed to main or merged PR)**
- List items with matching commits

**🔄 In Progress (local commits not yet on main)**
- List items with WIP commits

**⬜ Not Started**
- List remaining checklist items in priority order

**⚠️ Blocked**
- Anything with a dependency not yet resolved

5. Suggest what to work on next based on the dependency chain in `docs/sprint1.md`
