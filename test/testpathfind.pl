:-module(testpathfind, [testpathfind/0, tpf/0]).

:- use_module(library(testsupport)).
:-use_module(library(clipl)).

test(1, N) :-
	N= 'clear path 10 meters',
	start_test(N),
        test_assert(apiBotClientCmd(teleport('annies haven/129.044327/128.206070/81.519630/'))),
	time_limit(15 , stdGoto('annies haven/133.630234/132.717392/81.546028/')),
	needed(_,1,1),
	needed(_,1,2),
	\+ forbidden(_,1,_),
	\+ obstacle(_),
	\+ failure(1),
	end_test.

test(2, N) :-
	N= 'Zero Distance',
	start_test(N),
	test_assert(apiBotClientCmd(teleport(start_test_2))),
	time_limit(1 , stdGoto(stop_test_2)),
        \+ forbidden(_,_,_),
	\+ obstacle(_),
	end_test.


test(3, N) :-
         N= 'Go around obstacle',
         start_test(N),
         test_assert(apiBotClientCmd(teleport(start_test_3))),
         time_limit(25 , stdGoto(stop_test_3)),
         needed(_,3,1),
         \+ obstacle(_),
         \+ forbidden(_,3,1),
         end_test.


test(4, N) :-
	N= 'Goal Other Side Of Wall',
	start_test(N),
	start_test(N),
        test_assert(apiBotClientCmd(teleport(start_test_4))),
        time_limit(25 , stdGoto(stop_test_4)),
        needed(_,4,1),
	\+ obstacle(_),
	end_test.

test(5, N) :-
	N= 'on elevated path',
	start_test(N),
	test_assert(apiBotClientCmd(teleport('annies haven/149.389313/129.028732/85.411255/'))),
	time_limit(15 , apiBotClientCmd('follow*'('annies haven/156.894470/137.385620/85.394775/'))),
	\+ forbidden(_,5,_),
	end_test.


test(7, N) :-
	N = 'spiral tube',
	start_test(N),
	test_assert(apiBotClientCmd(teleport('annies haven/188.477066/142.809982/81.559509/'))),
	time_limit(60 , apiBotClientCmd('follow*'('annies haven/181.878403/140.768723/101.555061/'))),
	needed(_,7,1),
	end_test.


test(8, N) :-
	N = 'ground maze simple',
	start_test(N),
	test_assert(apiBotClientCmd(teleport('annies haven/4.813091/6.331439/27.287579/'))),
	time_limit(30 , apiBotClientCmd('follow*'('annies haven/26.930264/12.801470/27.149252/'))),
	needed(_,8,1),
	needed(_,8,2),
	end_test.


/*
test(3, N) :-
	N= 'Rotating Obstacle',
	start_test(N),
	apiBotClientCmd(teleport('annies haven/137.404724/187.234711/1000.985291/')),
	time_limit(15 , apiBotClientCmd('follow*'('annies haven/139.016434/206.675934/1000.985229/'))),
	needed(_,3,1),
	\+ obstacle(_),
	end_test.



/*
	keep this stuff, it's from the old opensim build, but it's a record
	of what tests we were doing

test(6, N) :-
	N= 'narrowest gap we can go through',
	start_test(N),
	apiBotClientCmd(teleport('annies haven/150.241486/131.945526/1000.985291/')),
	time_limit(15 , apiBotClientCmd('follow*'('annies haven/148.898590/146.752121/1000.988281/'))),
	\+ obstacle(_),
	\+ forbidden(_,_,_),
	end_test.

test(6, N) :-
	N= 'tortured prim tube',
	start_test(N),
	apiBotClientCmd(teleport('annies haven/236.392776/245.958130/1000.986572/')),
	time_limit(20 , apiBotClientCmd('follow*'('annies haven/239.544891/232.117767/1000.987122/'))),
	end_test.

test(7, N) :-
	N= 'jagged maze',
	start_test(N),
	apiBotClientCmd(teleport('annies haven/233.436218/221.673218/1000.988770/')),
	time_limit(60 , apiBotClientCmd('follow*'('annies haven/248.193939/190.898941/1000.985291/'))),
	\+ obstacle(_),
	end_test.

*/
testpathfind :-
	cliSet('SimAvatarImpl','UseTeleportFallback','@'(false)),
	test(_,_),
	fail.

tpf :-
        cliSet('SimAvatarImpl','UseTeleportFallback','@'(false)),
	clause(testpathfind:test(N,S),Cs),
        doTest(N,S,Cs),
	fail.
tpf:-!.

