namespace MeetingTranscriber.Presentation;

// What the application says on the screen that files a meeting under what it was about.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
    // Nothing in this file is a technical name and none of it may become one. The whole of what
    // #105 asks for is that a meeting is filed without the person meeting the three-level tree, the
    // closed vocabularies or anything else the corpus stores — so the columns are plain Spanish
    // about the meeting, and the words *node*, *role*, *link* and *template* appear nowhere.
    public static UiText WhatThisMeetingWasAbout { get; } =
        new("De qué fue esta reunión", "What this meeting was about");

    // The fourteen chips, and the question above them. What each one fills is not explained: it is
    // seen when it is chosen, which is what #105 settled.
    public static UiText WhichOneWasItLike { get; } = new("¿A cuál se pareció?", "Which one was it like?");

    // The three columns. Plain Spanish about the meeting and never the name of a role: *es trabajo
    // de*, *del otro lado*, *trata sobre*.
    public static UiText ItIsWorkOf { get; } = new("Es trabajo de", "It is work of");

    public static UiText TheOtherSide { get; } = new("Del otro lado", "The other side");

    public static UiText ItIsAbout { get; } = new("Trata sobre", "It is about");

    // Where the pills would be while a column has none. *Agregar* stands under the column either
    // way, so a column a shape opened empty is still one somebody can fill by hand.
    public static UiText NothingElse { get; } = new("Nada más", "Nothing else");

    public static UiText Add { get; } = new("Agregar", "Add");

    // The `+` at the end of a path. A glyph with no name is nothing to a screen reader, so this is
    // what that press is called in the automation tree rather than on it.
    public static UiText AddALevel { get; } = new("Agregar un nivel", "Add a level");

    public static UiText Who { get; } = new("Quiénes", "Who");

    // The two toggles on a person's row, one per way a meeting can name somebody. Both are
    // pressable because both are things somebody has to be able to say: §5.3 row 10 is a person a
    // meeting is about who was never in the room.
    public static UiText TheyWereThere { get; } = new("estuvo", "they were there");

    // Deliberately not the artboard's *la reunión es sobre ella*. A toggle drawn beside every row
    // cannot know whose row it is on, and a gendered pronoun there is wrong for half the corpus.
    public static UiText TheMeetingIsAboutThisPerson { get; } =
        new("la reunión es sobre esta persona", "the meeting is about them");

    public static UiText AddSomebody { get; } = new("Agregar a alguien", "Add somebody");

    // Not an escape. §5.3 says a casual chat is stored with no links and is found by text, so this
    // is an answer somebody gives — it empties the screen, and *Guardar* is still what writes.
    public static UiText LeaveItUnclassified { get; } =
        new("Dejarla sin clasificar", "Leave it unclassified");

    // Putting a filled classification by under a name, to file with again. A new act and not
    // *Conservar*'s, which is not throwing a recording away.
    public static UiText Remember { get; } = new("Recordar", "Remember");

    public static UiText ANameToUseItAgainBy { get; } =
        new("Un nombre para volver a usarla", "A name to use it again by");

    // Two entries and not a question with a noun in it. What stands at the top of the tree is either
    // an organization or a body of work belonging to nobody in particular, and #105's rule is that
    // no technical name appears on this screen — so the two are offered as the two things they are,
    // in the words somebody would use for them.
    public static UiText ANewOrganization { get; } = new("Una organización nueva…", "A new organization…");

    public static UiText WorkThatIsNobodysInParticular { get; } =
        new("Un trabajo que no es de nadie en particular…", "Work that is nobody's in particular…");

    // The title of the notice that adds a person, when it is opened over somebody who is already
    // there. A title only, which is why it has no ellipsis: what was pressed to get here said
    // *Corregir este nombre…*, and this says what the form in front of somebody is about.
    public static UiText AboutThisPerson { get; } = new("Sobre esta persona", "About this person");

    // What the dialogue that adds a person asks. An organization and a year are optional: a person
    // carries as many affiliations as they have, and a corpus that never learned the date has none.
    public static UiText NameOfAPerson { get; } = new("Nombre", "Name");

    // Offered under the name field while a person is being added and somebody the corpus holds is
    // spelled nearly the same way. The pill reads the name and the organization, and both are the
    // corpus's and not the catalogue's: only the separator is.
    public static UiText IsItSomebodyAlreadyHere { get; } = new("¿Es alguien que ya está?", "Is it somebody already here?");

    public static UiText SomebodyAndWhereTheyBelong { get; } = new("{0} · {1}", "{0} · {1}");

    public static UiText Organization { get; } = new("Organización", "Organization");

    public static UiText From { get; } = new("Desde", "From");

    // Where somebody belonged the day of the meeting, beside their name. The year and not the date:
    // what it is read against is another person's period and the meeting's own.
    public static UiText SinceTheYear { get; } = new("desde {0}", "since {0}");

    // The fourteen shapes, by name only. What each one fills is seen when it is chosen.
    public static UiText TheShapeClass { get; } = new("Clase", "Class");

    public static UiText TheShapeCasualCatchUp { get; } = new("Junta casual", "A casual catch-up");

    public static UiText TheShapeInterviewAsCandidate { get; } =
        new("Entrevista — soy el candidato", "Interview — I am the candidate");

    public static UiText TheShapeInterviewAsInterviewer { get; } =
        new("Entrevista — yo entrevisto", "Interview — I am interviewing");

    public static UiText TheShapeTwoProjects { get; } = new("Dos proyectos", "Two projects");

    public static UiText TheShapeSellingToAClient { get; } =
        new("Vendedor con cliente", "Salesperson with a client");

    public static UiText TheShapeTeamMeeting { get; } = new("Reunión de equipo", "Team meeting");

    public static UiText TheShapeConference { get; } = new("Conferencia", "Conference");

    public static UiText TheShapeBetweenTwoCompanies { get; } =
        new("Entre dos empresas", "Between two companies");

    public static UiText TheShapeHumanResources { get; } = new("Recursos humanos", "Human resources");

    public static UiText TheShapeRecurringOneToOne { get; } = new("1:1 recurrente", "Recurring 1:1");

    // The same either way, and it is named in UiTextsTests as one of those: a daily is called a
    // daily in both, the way the channel chips and the engine's name are.
    public static UiText TheShapeDaily { get; } = new("Daily", "Daily");

    public static UiText TheShapeAfterSalesSupport { get; } =
        new("Soporte post-venta", "After-sales support");

    // Not the same answer as *Junta casual*, which is why both are here. That one is «this was a
    // casual catch-up»; this one is «none of the thirteen fits and I will fill it in».
    public static UiText TheShapeFilledByHand { get; } =
        new("Ninguna — la lleno yo", "None — I will fill it in");
}
