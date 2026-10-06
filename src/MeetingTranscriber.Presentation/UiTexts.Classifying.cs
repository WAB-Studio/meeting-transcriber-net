namespace MeetingTranscriber.Presentation;

// What the application says on the screen that files a meeting under what it was about.
// One class with the others: see `UiTexts.cs`.
public static partial class UiTexts
{
    // Nothing in this file is a technical name and none of it may become one. The whole of what
    // #105 asks for is that a meeting is filed without the person meeting the three-level tree, the
    // closed vocabularies or anything else the corpus stores — so the columns are plain Spanish
    // about the meeting, and the words *node*, *role*, *link* and *template* appear nowhere.
    // The fourteen chips, and the question above them. What each one fills is not explained: it is
    // seen when it is chosen, which is what #105 settled.
    public static UiText WhichOneWasItLike { get; } = new("¿A cuál se pareció?", "Which one was it like?");

    // The heading of the column for what a meeting was about without being work of. The other two
    // columns are headed by the name of their first level, which is the meeting's own word for it
    // (see the `Place…` entries below) and no longer a sentence about the role.
    public static UiText ItIsAbout { get; } = new("Trata sobre", "It is about");

    // The two columns a person fills by hand reach through a press each, because with no kind of
    // meeting lit nothing has opened them. They are said as what is being added.
    public static UiText OpenOtherOrganization { get; } =
        new("Otra organización…", "Other organization…");

    public static UiText OpenItIsAbout { get; } = new("Trata sobre…", "It is about…");

    // What a level of a path, or a place for somebody, is called for the meeting it is in. The
    // names that already exist are used as they are — *Organización*, *Persona*, *Conferencia* —
    // so the same word is one entry; these are the rest. Each is a plain word somebody says out
    // loud and none is the name of what the corpus stores.
    public static UiText PlaceProject { get; } = new("Proyecto", "Project");

    public static UiText PlaceTopic { get; } = new("Asunto", "Topic");

    public static UiText PlaceUniversity { get; } = new("Universidad", "University");

    public static UiText PlaceCourse { get; } = new("Materia", "Course");

    public static UiText PlaceTeacher { get; } = new("Profesor", "Teacher");

    public static UiText PlaceCompany { get; } = new("Empresa", "Company");

    public static UiText PlaceInterviewer { get; } = new("Entrevistador", "Interviewer");

    public static UiText PlaceCandidate { get; } = new("Candidato", "Candidate");

    public static UiText PlaceClient { get; } = new("Cliente", "Client");

    public static UiText PlaceContact { get; } = new("Contacto", "Contact");

    public static UiText PlaceOrganizer { get; } = new("Organizador", "Organizer");

    public static UiText PlaceTeam { get; } = new("Equipo", "Team");

    public static UiText PlaceCase { get; } = new("Caso", "Case");

    public static UiText PlaceOtherOrganization { get; } =
        new("Otra organización", "Other organization");

    // Where the pills would be while a column has none. *Agregar* stands under the column either
    // way, so a column a shape opened empty is still one somebody can fill by hand.
    public static UiText NothingElse { get; } = new("Nada más", "Nothing else");

    public static UiText Add { get; } = new("Agregar", "Add");

    public static UiText Who { get; } = new("Quiénes", "Who");

    // The two toggles on a person's row, one per way a meeting can name somebody. Both are
    // pressable because both are things somebody has to be able to say: §5.3 row 10 is a person a
    // meeting is about who was never in the room.
    public static UiText TheyWereThere { get; } = new("estuvo", "was there");

    // Deliberately not the artboard's *la reunión es sobre ella*. A toggle drawn beside every row
    // cannot know whose row it is on, and a gendered pronoun there is wrong for half the corpus.
    public static UiText TheMeetingIsAboutThisPerson { get; } =
        new("tema", "topic");

    public static UiText AddSomebody { get; } = new("Agregar a alguien", "Add somebody");

    // Not an escape. §5.3 says a casual chat is stored with no links and is found by text, so this
    // is an answer somebody gives — it empties the screen, and *Guardar* is still what writes.
    public static UiText LeaveItUnclassified { get; } =
        new("No clasificar", "Do not classify");

    // Putting a filled classification by under a name, to file with again. A new act and not
    // *Conservar*'s, which is not throwing a recording away.
    public static UiText Remember { get; } = new("Recordar", "Remember");

    public static UiText ANameToUseItAgainBy { get; } =
        new("Recordar como…", "Remember as…");

    // Two entries and not a question with a noun in it. What stands at the top of the tree is either
    // an organization or a body of work belonging to nobody in particular, and #105's rule is that
    // no technical name appears on this screen — so the two are offered as the two things they are,
    // in the words somebody would use for them.
    public static UiText ANewOrganization { get; } = new("Organización nueva…", "New organization…");

    public static UiText WorkThatIsNobodysInParticular { get; } =
        new("Proyecto nuevo…", "New project…");

    // The title of the notice that adds a person, when it is opened over somebody who is already
    // there. A title only, which is why it has no ellipsis: what was pressed to get here said
    // *Corregir este nombre…*, and this says what the form in front of somebody is about.
    public static UiText AboutThisPerson { get; } = new("Persona", "Person");

    // What the dialogue that adds a person asks. An organization and a year are optional: a person
    // carries as many affiliations as they have, and a corpus that never learned the date has none.
    public static UiText NameOfAPerson { get; } = new("Nombre", "Name");

    // Offered under the name field while a person is being added and somebody the corpus holds is
    // spelled nearly the same way. The pill reads the name and the organization, and both are the
    // corpus's and not the catalogue's: only the separator is.
    public static UiText IsItSomebodyAlreadyHere { get; } = new("¿Ya existe?", "Already exists?");

    public static UiText SomebodyAndWhereTheyBelong { get; } = new("{0} · {1}", "{0} · {1}");

    public static UiText Organization { get; } = new("Organización", "Organization");

    // The year field of the dialogue that adds a person: its header says what the number is, and the
    // placeholder says what to type, so neither stands alone as one word that explains nothing.
    public static UiText SinceYear { get; } = new("Desde el año", "Since year");

    public static UiText Year { get; } = new("Año", "Year");

    // The first entry of the organization picker on that dialogue. Not *Ninguno*, which empties a
    // pill on the screens: here nothing is being emptied, a person is simply not tied to one.
    public static UiText NoOrganization { get; } = new("Ninguna", "None");

    // Where somebody belonged the day of the meeting, beside their name. The year and not the date:
    // what it is read against is another person's period and the meeting's own.
    public static UiText SinceTheYear { get; } = new("desde {0}", "since {0}");

    // The fourteen shapes, by name. What each one fills is seen when it is chosen; what it is is
    // said in a line under the pointer (see the `Describes…` entries below).
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

    // One line for each of the fourteen, shown on hover: what kind of meeting it is, in words, and
    // never what it will open.
    public static UiText DescribesClass { get; } = new("Una clase o curso", "A class or course");

    public static UiText DescribesCasualCatchUp { get; } =
        new("Una charla sin tema fijo", "A chat with no set topic");

    public static UiText DescribesInterviewAsCandidate { get; } =
        new("Te entrevista una empresa", "A company interviews you");

    public static UiText DescribesInterviewAsInterviewer { get; } =
        new("Entrevistas a alguien", "You interview somebody");

    public static UiText DescribesTwoProjects { get; } =
        new("Trata dos proyectos a la vez", "Covers two projects at once");

    public static UiText DescribesSellingToAClient { get; } =
        new("Le vendes a un cliente", "You sell to a client");

    public static UiText DescribesTeamMeeting { get; } = new("Tu propio equipo", "Your own team");

    public static UiText DescribesConference { get; } =
        new("Una charla o evento externo", "A talk or outside event");

    public static UiText DescribesBetweenTwoCompanies { get; } =
        new("Dos empresas, ninguna la tuya", "Two companies, neither yours");

    public static UiText DescribesHumanResources { get; } =
        new("Sobre alguien que no está", "About somebody who is not there");

    public static UiText DescribesRecurringOneToOne { get; } =
        new("Uno a uno con la misma persona", "One to one with the same person");

    public static UiText DescribesDaily { get; } =
        new("La reunión diaria del equipo", "The team's daily meeting");

    public static UiText DescribesAfterSalesSupport { get; } =
        new("Un caso de un cliente", "A client's case");

    public static UiText DescribesFilledByHand { get; } = new("Lo llenas tú", "You fill it in");
}
