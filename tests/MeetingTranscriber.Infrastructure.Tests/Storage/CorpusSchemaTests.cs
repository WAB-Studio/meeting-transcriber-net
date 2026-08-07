using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

public class CorpusSchemaTests
{
    private const string MeetingId = "11111111-1111-1111-1111-111111111111";
    private const string When = "2026-08-05T14:00:00.000Z";
    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>A real one, for the tests that read a job back through the model.</summary>
    private const string JobId = "44444444-4444-4444-4444-444444444444";

    [Fact]
    public void A_connection_arrives_with_foreign_keys_on_and_the_file_in_wal()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Sql.Scalar(context, "PRAGMA foreign_keys;").ShouldBe(1L);
        Sql.Scalar(context, "PRAGMA journal_mode;").ShouldBe("wal");
    }

    /// <summary>
    /// Deleting a node takes the tree under it and the affiliations to it, and nothing else. The
    /// work is inside the node, so it goes; a person is not, so they stay and only lose the link.
    /// </summary>
    [Fact]
    public void Deleting_a_node_takes_what_hangs_under_it_and_leaves_the_people()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertRoot(context, "o", "organization", "An Organization");
        InsertChild(context, "i", parent: "o", parentKind: "organization", parentDepth: 0, "initiative", "the work");
        InsertPerson(context, "h");
        InsertAffiliation(context, "a", person: "h", organization: "o", organizationKind: "organization");
        Sql.Execute(context, "DELETE FROM nodes WHERE id = 'o';");

        Sql.Scalar(context, "SELECT count(*) FROM nodes;").ShouldBe(0L);
        Sql.Scalar(context, "SELECT count(*) FROM people;").ShouldBe(1L);
        Sql.Scalar(context, "SELECT count(*) FROM affiliations;").ShouldBe(0L);
    }

    [Fact]
    public void A_node_cannot_sit_deeper_than_the_tree_goes()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertRoot(context, "o", "organization", "root");
        InsertChild(context, "i", parent: "o", parentKind: "organization", parentDepth: 0, "initiative", "the work");

        Should.Throw<SqliteException>(() => InsertChild(
            context, "d", parent: "i", parentKind: "initiative", parentDepth: 1, "topic", "too deep", depth: 3));
    }

    /// <summary>
    /// One level down, and no other. A child says what its parent's depth is, the foreign key
    /// makes that the parent's own, and a CHECK ties it to the child's — so a node landing at the
    /// wrong depth is refused by the database and not by whoever remembered to compute it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void A_child_sits_exactly_one_level_below_its_parent(int depth)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertRoot(context, "o", "organization", "root");

        Should.Throw<SqliteException>(() => InsertChild(
            context, "x", parent: "o", parentKind: "organization", parentDepth: 0, "initiative", "wrong depth", depth));
    }

    /// <summary>
    /// The copy a child keeps of its parent has to be the parent's own. Without the key it is a
    /// pair of columns anybody can write anything into, and the depth check above would pass over
    /// a parent that is nowhere near where the child says it is.
    /// </summary>
    [Fact]
    public void A_child_cannot_invent_the_parent_it_says_it_has()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertRoot(context, "o", "organization", "root");
        InsertChild(context, "i", parent: "o", parentKind: "organization", parentDepth: 0, "initiative", "the work");

        // 'i' is an initiative at depth 1, and this child claims it is an organization at depth 0.
        Should.Throw<SqliteException>(() => InsertChild(
            context, "x", parent: "i", parentKind: "organization", parentDepth: 0, "initiative", "invented"));
    }

    /// <summary>
    /// The classes go organization, initiative, topic, and the tree is where that is said. A topic
    /// standing on its own is a subject of nothing; an organization under one is the order upside
    /// down.
    /// </summary>
    [Fact]
    public void A_topic_is_never_a_root()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<SqliteException>(() => InsertRoot(context, "t", "topic", "an incident"));
    }

    [Fact]
    public void An_organization_never_hangs_off_a_topic()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertRoot(context, "o", "organization", "root");
        InsertChild(context, "i", parent: "o", parentKind: "organization", parentDepth: 0, "initiative", "the work");
        InsertChild(context, "t", parent: "i", parentKind: "initiative", parentDepth: 1, "topic", "an incident");

        Should.Throw<SqliteException>(() => InsertChild(
            context, "x", parent: "t", parentKind: "topic", parentDepth: 2, "organization", "upside down"));
    }

    /// <summary>
    /// Where somebody belongs is an organization. A project and a ticket are places work happens,
    /// and the class travels with the id so neither has anywhere to be written.
    /// </summary>
    [Fact]
    public void A_person_is_at_an_organization_and_not_at_a_project()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertRoot(context, "o", "organization", "An Organization");
        InsertChild(context, "i", parent: "o", parentKind: "organization", parentDepth: 0, "initiative", "the work");
        InsertPerson(context, "h");

        Should.NotThrow(() => InsertAffiliation(context, "a", "h", organization: "o", organizationKind: "organization"));
        // The initiative, correctly named as one: the CHECK refuses the class outright.
        Should.Throw<SqliteException>(() => InsertAffiliation(context, "b", "h", "i", "initiative"));
        // And the same initiative, dressed up as an organization: no row matches the key either.
        Should.Throw<SqliteException>(() => InsertAffiliation(context, "c", "h", "i", "organization"));
    }

    /// <summary>
    /// The case the single column could not hold: a contractor at two clients at once. What is
    /// refused is the same one twice while both are open, which is one fact written down twice.
    /// </summary>
    [Fact]
    public void Somebody_is_at_two_organizations_at_once_but_never_at_one_twice()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertRoot(context, "o", "organization", "An Organization");
        InsertRoot(context, "p", "organization", "A Client");
        InsertPerson(context, "h");

        Should.NotThrow(() => InsertAffiliation(context, "a", "h", "o", "organization"));
        Should.NotThrow(() => InsertAffiliation(context, "b", "h", "p", "organization"));
        Should.Throw<SqliteException>(() => InsertAffiliation(context, "c", "h", "o", "organization"));

        // Closed, so being there again is a second spell and not a duplicate of the first.
        Sql.Execute(context, $"UPDATE affiliations SET ended_at = '{When}' WHERE id = 'a';");
        Should.NotThrow(() => InsertAffiliation(context, "d", "h", "o", "organization"));
    }

    [Fact]
    public void An_affiliation_cannot_end_before_it_started()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertRoot(context, "o", "organization", "An Organization");
        InsertPerson(context, "h");

        Should.Throw<SqliteException>(() => InsertAffiliation(
            context, "a", "h", "o", "organization", startedAt: When, endedAt: "2026-08-04T14:00:00.000Z"));
    }

    /// <summary>
    /// Somebody's own one to one: they were there, and it was about them. One row per person made
    /// that a choice between two true things, and whichever was not picked was lost.
    /// </summary>
    [Fact]
    public void A_meeting_can_be_attended_by_the_person_it_is_about()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertMeeting(context);
        InsertPerson(context, "h");

        Should.NotThrow(() => InsertMeetingPerson(context, "h", "attended"));
        Should.NotThrow(() => InsertMeetingPerson(context, "h", "subject"));
        Sql.Scalar(context, "SELECT count(*) FROM meeting_people;").ShouldBe(2L);
    }

    [Fact]
    public void A_meeting_cannot_name_a_person_in_a_way_the_domain_does_not_have()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertMeeting(context);
        InsertPerson(context, "h");

        Should.Throw<SqliteException>(() => InsertMeetingPerson(context, "h", "sort_of_there"));
    }

    /// <summary>A root is exactly what has no parent, in both directions.</summary>
    [Fact]
    public void A_node_is_a_root_or_it_is_not()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        // No parent, and claiming to be one level down.
        Should.Throw<SqliteException>(() => Sql.Execute(context, $"""
            INSERT INTO nodes (id, parent_id, parent_kind, parent_depth, kind, name, depth, created_at, updated_at)
            VALUES ('x', NULL, NULL, NULL, 'initiative', 'confused', 1, '{When}', '{When}');
            """));

        // A parent, and claiming to be a root.
        InsertRoot(context, "o", "organization", "root");
        Should.Throw<SqliteException>(() => InsertChild(
            context, "y", parent: "o", parentKind: "organization", parentDepth: 0, "initiative", "confused", depth: 0));
    }

    /// <summary>
    /// SQLite counts NULLs as distinct, so the index over (parent, name) lets two roots share a
    /// name. The filtered one is what actually stops a second organization called the same thing.
    /// </summary>
    [Fact]
    public void Two_roots_cannot_share_a_name()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertRoot(context, "a", "organization", "TwoOfThese");

        Should.Throw<SqliteException>(() => InsertRoot(context, "b", "organization", "TwoOfThese"));
    }

    /// <summary>
    /// The case the single project column could not hold: one meeting, two bodies of work, both
    /// searchable. It is two rows, not a choice between them.
    /// </summary>
    [Fact]
    public void A_meeting_can_be_work_of_two_things_at_once()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertMeeting(context);
        InsertRoot(context, "o", "organization", "An Organization");
        InsertChild(context, "a", parent: "o", parentKind: "organization", parentDepth: 0, "initiative", "coati");
        InsertChild(context, "b", parent: "o", parentKind: "organization", parentDepth: 0, "initiative", "huemul");
        Sql.Execute(context, $"""
            INSERT INTO meeting_nodes (meeting_id, node_id, role, created_at)
            VALUES ('{MeetingId}', 'a', 'work_of', '{When}');
            INSERT INTO meeting_nodes (meeting_id, node_id, role, created_at)
            VALUES ('{MeetingId}', 'b', 'work_of', '{When}');
            """);

        Sql.Scalar(context, "SELECT count(*) FROM meeting_nodes;").ShouldBe(2L);
    }

    [Fact]
    public void A_meeting_cannot_relate_to_a_node_in_a_way_the_domain_does_not_have()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        InsertMeeting(context);
        InsertRoot(context, "o", "organization", "An Organization");

        Should.Throw<SqliteException>(() => Sql.Execute(context, $"""
            INSERT INTO meeting_nodes (meeting_id, node_id, role, created_at)
            VALUES ('{MeetingId}', 'o', 'sort_of_about', '{When}');
            """));
    }

    [Fact]
    public void An_artifact_cannot_belong_to_a_meeting_that_is_not_there()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<SqliteException>(() => Sql.Execute(context, $"""
            INSERT INTO artifacts (id, meeting_id, kind, origin, relative_path, byte_size, sha256, created_at)
            VALUES ('a', 'no-such-meeting', 'audio', 'source', 'audio.wav', 1, '{Sha256}', '{When}');
            """));
    }

    [Fact]
    public void A_meeting_cannot_hold_a_source_profile_the_domain_does_not_know()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<SqliteException>(() => InsertMeeting(context, sourceProfile: "stereo"));
    }

    [Fact]
    public void A_meeting_cannot_hold_a_lifecycle_state_that_is_not_one_of_the_three()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<SqliteException>(() => InsertMeeting(context, lifecycleState: "transcribed"));
    }

    [Fact]
    public void An_active_meeting_cannot_carry_a_deletion_date()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.Throw<SqliteException>(() => Sql.Execute(
            context,
            $"UPDATE meetings SET deleted_at = '{When}' WHERE id = '{MeetingId}';"));
    }

    [Fact]
    public void A_turn_can_only_sit_on_the_meeting_channel_the_user_channel_or_neither()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.NotThrow(() => InsertUtterance(context, id: "u0", ordinal: 0, channel: "0"));
        Should.NotThrow(() => InsertUtterance(context, id: "u1", ordinal: 1, channel: "1"));
        Should.NotThrow(() => InsertUtterance(context, id: "u2", ordinal: 2, channel: "NULL"));
        Should.Throw<SqliteException>(() => InsertUtterance(context, id: "u3", ordinal: 3, channel: "2"));
    }

    [Fact]
    public void A_turn_cannot_end_before_it_starts()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.Throw<SqliteException>(() => Sql.Execute(context, $"""
            INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
            VALUES ('u', '{MeetingId}', 0, 5000, 4000, 0, 'channel_0', 'backwards');
            """));
    }

    [Theory]
    [InlineData("deepgram_response", "derived")]
    [InlineData("audio", "derived")]
    [InlineData("transcript", "source")]
    [InlineData("summary", "source")]
    public void An_artifact_cannot_claim_the_wrong_side_of_the_source_line(string kind, string origin)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.Throw<SqliteException>(() => InsertArtifact(context, kind, origin));
    }

    [Theory]
    [InlineData("deepgram_response", "source")]
    [InlineData("audio", "source")]
    [InlineData("manifest", "source")]
    [InlineData("transcript", "derived")]
    [InlineData("summary", "derived")]
    public void An_artifact_on_the_right_side_of_the_line_goes_in(string kind, string origin)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.NotThrow(() => InsertArtifact(context, kind, origin));
    }

    [Fact]
    public void A_job_cannot_sit_in_a_state_the_design_does_not_have()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.NotThrow(() => InsertJob(context, id: "j0", state: "awaiting_user"));
        Should.Throw<SqliteException>(() => InsertJob(context, id: "j1", state: "in_progress"));
    }

    /// <summary>
    /// A job waiting for a person says why, and a job that is not waiting says nothing. Which
    /// matters because the reason used to be written into the error column: a cost still to
    /// approve is not a failure, and that column is the one a screen reads to say what happened.
    /// </summary>
    [Fact]
    public void Only_a_job_waiting_for_a_person_carries_a_reason_for_waiting()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);

        Should.Throw<SqliteException>(() => InsertJob(
            context, id: "j0", state: "awaiting_user", defaultReason: false));
        Should.Throw<SqliteException>(() => InsertJob(
            context, id: "j1", state: "running", awaitingReason: "not waiting for anybody"));
    }

    /// <summary>
    /// Where a run stands is its job's, and the job cannot be taken out from under it: a call
    /// somebody paid for whose state nothing holds is worse than one that refuses to be deleted.
    /// </summary>
    [Fact]
    public void A_run_cannot_lose_the_job_that_says_where_it_stands()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);
        InsertJob(context, id: "j0", state: "running");
        InsertTranscriptionRun(context, id: "t0", job: "'j0'");

        Should.Throw<SqliteException>(() => Sql.Execute(context, "DELETE FROM processing_jobs WHERE id = 'j0';"));

        // A run with no job at all has nowhere to say where it stands either.
        Should.Throw<SqliteException>(() => InsertTranscriptionRun(context, id: "t1", job: "NULL"));
    }

    /// <summary>
    /// And the refusal above must not cost the one deletion the corpus is built for: the meeting
    /// takes its jobs and its runs in the same statement, which is when the constraint is checked.
    /// </summary>
    [Fact]
    public void Deleting_a_meeting_still_takes_its_jobs_and_the_runs_that_point_at_them()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);
        InsertJob(context, id: "j0", state: "succeeded");
        InsertTranscriptionRun(context, id: "t0", job: "'j0'");

        Sql.Execute(context, $"DELETE FROM meetings WHERE id = '{MeetingId}';");

        Sql.Scalar(context, "SELECT count(*) FROM processing_jobs;").ShouldBe(0L);
        Sql.Scalar(context, "SELECT count(*) FROM transcription_runs;").ShouldBe(0L);
    }

    /// <summary>
    /// The second answer that used to exist. A run held the same seven-state vocabulary as its
    /// job, writable to anything, and the two disagreed the moment one of them was not updated.
    /// </summary>
    [Fact]
    public void A_run_has_no_state_of_its_own_to_disagree_with_its_job()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        foreach (var table in new[] { "transcription_runs", "extraction_runs" })
        {
            Sql.Strings(context, $"SELECT name FROM pragma_table_info('{table}');")
                .ShouldNotContain("state", table);
        }
    }

    /// <summary>
    /// What a restart does to a run: nothing, because there is nothing to do. The job it belongs
    /// to is where it stands, so recovering the job is what moves the run — and a run left in
    /// 'running' for ever is a state that no longer exists to be left in.
    /// </summary>
    [Fact]
    public void A_restart_leaves_a_run_wherever_it_leaves_the_job()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);
        InsertJob(context, id: JobId, state: "running");
        InsertTranscriptionRun(context, id: "t0", job: $"'{JobId}'");

        var job = context.ProcessingJobs.Single();
        job.RecoverAfterRestart().ShouldBeTrue();
        context.SaveChanges();

        Sql.Scalar(context, """
            SELECT job.state FROM transcription_runs AS run
            JOIN processing_jobs AS job ON job.id = run.job_id
            WHERE run.id = 't0';
            """).ShouldBe("awaiting_user");
    }

    [Fact]
    public void Deleting_a_meeting_takes_its_rows_with_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);
        InsertUtterance(context, id: "u0", ordinal: 0, channel: "0");
        InsertArtifact(context, "audio", "source");

        Sql.Execute(context, $"DELETE FROM meetings WHERE id = '{MeetingId}';");

        Sql.Scalar(context, "SELECT count(*) FROM utterances;").ShouldBe(0L);
        Sql.Scalar(context, "SELECT count(*) FROM artifacts;").ShouldBe(0L);
    }

    [Fact]
    public void Search_finds_a_turn_by_its_words_and_forgets_it_once_deleted()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);
        InsertUtterance(context, id: "u0", ordinal: 0, channel: "0", text: "the drift budget is fifty milliseconds");

        Sql.Scalar(context, "SELECT count(*) FROM utterances_fts WHERE utterances_fts MATCH 'drift';").ShouldBe(1L);
        Sql.Scalar(context, "SELECT count(*) FROM utterances_fts WHERE utterances_fts MATCH 'loopback';").ShouldBe(0L);

        Sql.Execute(context, "DELETE FROM utterances WHERE id = 'u0';");

        Sql.Scalar(context, "SELECT count(*) FROM utterances_fts WHERE utterances_fts MATCH 'drift';").ShouldBe(0L);
    }

    [Fact]
    public void Search_finds_a_summary_by_its_words_and_forgets_it_once_deleted()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        InsertMeeting(context);
        InsertSummary(context, "what the meeting settled", "the drift budget is fifty milliseconds");

        Sql.Scalar(context, "SELECT count(*) FROM summaries_fts WHERE summaries_fts MATCH 'settled';").ShouldBe(1L);
        Sql.Scalar(context, "SELECT count(*) FROM summaries_fts WHERE summaries_fts MATCH 'drift';").ShouldBe(1L);
        Sql.Scalar(context, "SELECT count(*) FROM summaries_fts WHERE summaries_fts MATCH 'loopback';").ShouldBe(0L);

        Sql.Execute(context, "DELETE FROM summaries;");

        Sql.Scalar(context, "SELECT count(*) FROM summaries_fts WHERE summaries_fts MATCH 'settled';").ShouldBe(0L);
    }

    /// <summary>How the MCP server reads a corpus somebody else may be writing.</summary>
    [Fact]
    public void A_read_only_corpus_reads_what_the_writer_left_and_refuses_to_add_to_it()
    {
        using var corpus = new TemporaryCorpus();

        using (var writing = corpus.OpenMigrated())
        {
            InsertMeeting(writing);
        }

        SqliteConnection.ClearAllPools();
        using var reading = CorpusDatabase.OpenReadOnly(corpus.DatabasePath);

        Sql.Scalar(reading, "SELECT count(*) FROM meetings;").ShouldBe(1L);
        Sql.Scalar(reading, "PRAGMA foreign_keys;").ShouldBe(1L);
        Should.Throw<SqliteException>(() => InsertMeeting(
            reading,
            id: "22222222-2222-2222-2222-222222222222"));
    }

    private static void InsertTranscriptionRun(CorpusDbContext context, string id, string job) =>
        Sql.Execute(context, $"""
            INSERT INTO transcription_runs (
                id, meeting_id, job_id, provider, source_profile, language, audio_sha256,
                billable_config_hash, created_at)
            VALUES ('{id}', '{MeetingId}', {job}, 'deepgram', 'multichannel', 'es', '{Sha256}',
                    'nova-3/multichannel', '{When}');
            """);

    private static void InsertRoot(CorpusDbContext context, string id, string kind, string name) =>
        Sql.Execute(context, $"""
            INSERT INTO nodes (id, parent_id, parent_kind, parent_depth, kind, name, depth, created_at, updated_at)
            VALUES ('{id}', NULL, NULL, NULL, '{kind}', '{name}', 0, '{When}', '{When}');
            """);

    /// <summary>
    /// A child, spelling out the copy of its parent it carries. The depth defaults to the one it
    /// should have, so a test that wants the wrong one has to say so.
    /// </summary>
    private static void InsertChild(
        CorpusDbContext context,
        string id,
        string parent,
        string parentKind,
        int parentDepth,
        string kind,
        string name,
        int? depth = null) =>
        Sql.Execute(context, $"""
            INSERT INTO nodes (id, parent_id, parent_kind, parent_depth, kind, name, depth, created_at, updated_at)
            VALUES ('{id}', '{parent}', '{parentKind}', {parentDepth}, '{kind}', '{name}',
                    {depth ?? (parentDepth + 1)}, '{When}', '{When}');
            """);

    private static void InsertPerson(CorpusDbContext context, string id) =>
        Sql.Execute(context, $"""
            INSERT INTO people (id, display_name, is_me, created_at, updated_at)
            VALUES ('{id}', 'Somebody', 0, '{When}', '{When}');
            """);

    /// <summary>
    /// Somebody at an organization, open at both ends unless the test says otherwise — which is
    /// what the corpus holds whenever nobody wrote the dates down.
    /// </summary>
    private static void InsertAffiliation(
        CorpusDbContext context,
        string id,
        string person,
        string organization,
        string organizationKind,
        string? startedAt = null,
        string? endedAt = null) =>
        Sql.Execute(context, $"""
            INSERT INTO affiliations (id, person_id, organization_id, organization_kind, started_at, ended_at, created_at)
            VALUES ('{id}', '{person}', '{organization}', '{organizationKind}',
                    {Quoted(startedAt)}, {Quoted(endedAt)}, '{When}');
            """);

    private static void InsertMeetingPerson(CorpusDbContext context, string person, string role) =>
        Sql.Execute(context, $"""
            INSERT INTO meeting_people (meeting_id, person_id, role, created_at)
            VALUES ('{MeetingId}', '{person}', '{role}', '{When}');
            """);

    private static string Quoted(string? value) => value is null ? "NULL" : $"'{value}'";

    private static void InsertMeeting(
        CorpusDbContext context,
        string id = MeetingId,
        string sourceProfile = "multichannel",
        string lifecycleState = "active") =>
        Sql.Execute(context, $"""
            INSERT INTO meetings (id, started_at, source_profile, language, lifecycle_state, created_at, updated_at)
            VALUES ('{id}', '{When}', '{sourceProfile}', 'es', '{lifecycleState}', '{When}', '{When}');
            """);

    private static void InsertUtterance(
        CorpusDbContext context,
        string id,
        int ordinal,
        string channel,
        string text = "anything at all") =>
        Sql.Execute(context, $"""
            INSERT INTO utterances (id, meeting_id, ordinal, start_ms, end_ms, channel, speaker_label, text)
            VALUES ('{id}', '{MeetingId}', {ordinal}, 0, 1000, {channel}, 'channel_0', '{text}');
            """);

    private static void InsertArtifact(CorpusDbContext context, string kind, string origin) =>
        Sql.Execute(context, $"""
            INSERT INTO artifacts (id, meeting_id, kind, origin, relative_path, byte_size, sha256, created_at)
            VALUES ('{kind}-{origin}', '{MeetingId}', '{kind}', '{origin}', '{kind}.{origin}', 1, '{Sha256}', '{When}');
            """);

    /// <summary>
    /// A summary needs the extraction run it came out of, and a run needs the job that ran it —
    /// which is the only row saying where it stands — so all three go in here.
    /// </summary>
    private static void InsertSummary(CorpusDbContext context, string @abstract, string body)
    {
        InsertJob(context, id: "j-extract", state: "succeeded");
        Sql.Execute(context, $"""
            INSERT INTO extraction_runs (id, meeting_id, job_id, provider, prompt_version, schema_version, input_hash, created_at)
            VALUES ('e0', '{MeetingId}', 'j-extract', 'claude_code', '1', '1', '{Sha256}', '{When}');
            """);

        Sql.Execute(context, $"""
            INSERT INTO summaries (id, meeting_id, extraction_run_id, abstract, body, created_at)
            VALUES ('s0', '{MeetingId}', 'e0', '{@abstract}', '{body}', '{When}');
            """);
    }

    /// <summary>
    /// A job. The reason defaults to whatever the state needs, so a test that wants the two out of
    /// step has to ask for it.
    /// </summary>
    private static void InsertJob(
        CorpusDbContext context,
        string id,
        string state,
        string? awaitingReason = null,
        bool defaultReason = true)
    {
        var reason = awaitingReason ?? (defaultReason && state == "awaiting_user" ? "a cost nobody approved" : null);
        Sql.Execute(context, $"""
            INSERT INTO processing_jobs (id, meeting_id, kind, state, awaiting_reason, idempotency_key, created_at, attempt)
            VALUES ('{id}', '{MeetingId}', 'transcribe', '{state}',
                    {(reason is null ? "NULL" : $"'{reason}'")}, '{id}', '{When}', 0);
            """);
    }
}
