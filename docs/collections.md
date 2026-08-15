---
description: Organize related Copilot sessions in Collections and reopen all or part of a Collection together.
---

# Collections

Collections are Narnia's single user-owned way to organize sessions. A Collection can span
repositories, and the same session can belong to more than one Collection.

Create or update a Collection from:

- selected results on the **Sessions** page;
- selected live sessions in **Runtime → Windows**; or
- the **Collections** page, where an empty Collection can be created before sessions are added.

## Opening a Collection

The Collections list and each Collection detail page can open every member together. By default,
Narnia opens the sessions as tabs in one Windows Terminal window. Enable **Separate windows** when
each session should open independently.

On a Collection detail page, select only the sessions needed and choose **Open Selected** to launch
part of the Collection with the same window-mode choice.

Collections do not currently expose manual tab ordering. Launches use the Collection's current
membership order; persistent drag-and-drop ordering can be added later without bringing back a
second organizational model.

## Retired Session Groups

Session Groups were an older, overlapping model for named sets of sessions. They are no longer
shown or modified by current Narnia versions:

- `/session-groups` and `/groups` redirect to Collections;
- the former Session Group APIs return HTTP `410 Gone`;
- legacy group membership no longer protects a session from cleanup or follows a recovered
  successor session.

Existing Session Group rows are left unchanged in `settings.db` rather than imported, renamed, or
deleted. This preserves downgrade safety and keeps existing Collections authoritative.
