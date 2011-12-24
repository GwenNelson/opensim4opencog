/*

 _________________________________________________________________________
|       Copyright (C) 1982                                                |
|                                                                         |
|       David Warren,                                                     |
|               SRI International, 333 Ravenswood Ave., Menlo Park,       |
|               California 94025, USA;                                    |
|                                                                         |
|       Fernando Pereira,                                                 |
|               Dept. of Architecture, University of Edinburgh,           |
|               20 Chambers St., Edinburgh EH1 1JZ, Scotland              |
|                                                                         |
|       This program may be used, copied, altered or included in other    |
|       programs only for academic purposes and provided that the         |
|       authorship of the initial program is aknowledged.                 |
|       Use for commercial purposes without the previous written          |
|       agreement of the authors is forbidden.                            |
|_________________________________________________________________________|

*/

terminal(T,S,S,x(_,terminal,T,X),X).
%   writef("           terminal -- SUCCEEDED1 for T= {0} S= {1}", [T,S])
%.
terminal(T,[T|S],S,X,X) :-
%   writef("           terminal -- SUCCEEDED2 for T= {0} S= {1}", [T,S]),
   gap(X).
%terminal(A,B,C,D,E) :-
%   writef("           terminal -- FAILED for T= {0} S= {1}", [A,B]), !, fail.

gap(x(gap,_,_,_)).
gap([]).

virtual(NT,x(_,nonterminal,NT,X),X).
