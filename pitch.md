# Narrated code review tool

I'm thinking about a project. At the end of the year, everyone always gets
excited about "Spotify Wrapped". There were a bunch of "Claude Wrapped" or
"Claude Compiled" projects that provided a similar view of how you had used
Claude Code based on logs on your machine. Cool stuff. I don't want to do
that. It's context.

More context (June 2025): <https://raw.githubusercontent.com/richlander/scra>tch/refs/heads/main/narrated-code-review/narrated-code-review.md

Imagine you you have a set of Claude Code an/or copilot sessions running.
Like in the doc, you are the CNC mill operating, but don't have any observability.
You start up another terminal app. It operates as a dashboard for both recent
and active sessions. Where it
cannot provide the desired experience purely mechanically, it calls back
into Claude Code (as an executable) to summarize or derive insight. There
is a lot of discussion about "Ralph Wiggum loop" recently. This wouldn't be
that, but would be similar in that the tool would control CC from the
outside.

I haven't done a lot of thinking on the UX. I think the best
experience would be that the primary experience
would be something closer to a nicer git log. I don't actually mean "show
git commits". I mean show logical changes (as a tile or 'card') as they come and then enable a
drill down into both diffs and something logically similar "Browse repository at this point".
The reason it's not a git thing (and this is likely obvious) is that
there would be two dozen (or more) changes leading to a commit. But much
of the same thinking applies. The app needs to materialize meaningful structure.

![Browse repository at this point](https://github.com/user-attachments/assets/9f7318d2-bb11-48e3-83dd-e5ba6bd621b5)

I'd want to support both copilot and claude. This would enable
providing stats about usage, time of date, and such things so that users understand their patters. THis would be Wrapped liked statistics, every day.

I write everything in C# and publish as Native AOT. For C#, there is spectre.
I'm not sure if that's a good option or not for this. You'd need to
investigate. For now, I want to come up with a plan.
Most of my colleagues use copilot so it might make sense to to start with it.
I have lots of copilot and claude logs on my machine.

There have been lots of efforts to build an agent dashboard. I'm not looking for anything fancy,
but what a "working developer" appreciates as another tool to help them be productive
and derive meaning in their "cnc mill shop".
