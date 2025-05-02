
--Psgsql_001_ver3.9.1.x_02.05.2025   Сервис формирования Файла Визуализации.sql
----------------------------------------------------------------------------------------------
--Автор: Валявский А.А.
--Дата создания скрипта: 02.05.2025
--Проблема:
--Релиз: 3.9.1.x
--------------------------------------------------------------------------------------------


--Psgsql_001_ver3.9.1.x_02.05.2025 Сервис формирования Файла Визуализации.sql

set search_path to dbo;


do
$$
BEGIN
    if(not exists (select null from grk_t_calling_microservice_worker where "Name" ilike 'FileMakerService')) then
        insert into grk_t_calling_microservice_worker("Name", "DisplayName")
        select 'FileMakerService', 'Сервис формирования Файла визуализации';
    end if;
    if(not exists (select null from grk_t_calling_microservice_source where "Name" ilike 'grk_tiu_ldstamp')) then
        insert into grk_t_calling_microservice_source("Name", "DisplayName")
        select 'grk_tiu_ldstamp', 'Триггер на таблице LDStamp';
    end if;
end;
$$;

drop function if exists dbo.grk_sp_filemakerservice_restorerights;
create or replace function dbo.grk_sp_filemakerservice_restorerights(oldFileID integer, newFileID integer)
returns integer
language plpgsql
as
$$
begin
    delete from ldrightobjectex rex where rex."ObjectID"=newFileID;

    insert into ldrightobjectex("ObjectID", "RightID", "MemberID", "DenyRight")
    select newFileID, rex."RightID",rex."MemberID",rex."DenyRight"
    from ldrightobjectex rex
    where rex."ObjectID"=oldFileID;
    return 0;
end;
$$;
alter function dbo.grk_sp_filemakerservice_restorerights(integer, integer)owner to dbo;
do $$
declare
    tmpScriptName text;
    tmpScriptDescr text;
    tmpErrorMsg text;
    tmpErrorText text;
    tmpErrorContext text;
    tmpObjectTypeID bigint;
    tmpObjectTypeName text;
    tmpProcID bigint;
    tmpProcName text;
    tmpInternalName text;
    tmpReturnType bigint;
begin
    tmpScriptName :=  'Регистрация LanDocs3 метода grk_sp_filemakerservice_restorerights';
    tmpScriptDescr := 'Автор: Валявский А.А. Дата:02.05.2025   Сервис формирования Файла Визуализации';
    tmpProcID := null;
    tmpInternalName := 'grk_sp_filemakerservice_restorerights';
    tmpProcName := 'grk_sp_filemakerservice_restorerights';
    tmpObjectTypeName := 'GRK_CALLING_MICROSERVICE_QUEUE';
    tmpObjectTypeID := null;
    tmpReturnType := 1;

    --Определяем идентификатор типа данных
    select "ID" from dbo.ldobjecttype where upper("Name") = upper(tmpObjectTypeName) into tmpObjectTypeID;
    if tmpObjectTypeID is null then
        tmpErrorMsg := 'Тип данных "' || tmpObjectTypeName || '" не найден в таблице ldobjecttype';
        raise exception '%',tmpErrorMsg;
    end if;

    delete from dbo.ld3method where upper("Name") = upper(tmpProcName) and "ObjectTypeID" = tmpObjectTypeID;
    select max("ID") + 1 from dbo.LD3Method into tmpProcID;
    tmpProcID := coalesce(tmpProcID, 1);
    insert into dbo.ld3method ("ID", "Name", "InternalName", "ObjectTypeID", "Deleted", "MethodType", "ReturnType", "ReturnList","ListColumn")
    values (tmpProcID,upper(tmpProcName),upper(tmpInternalName),tmpObjectTypeID,'-','S',tmpReturnType,'-',NULL);

    delete from dbo.ld3methodparameter where "MethodID" = tmpProcID;
    --Добавляем параметры
    insert into dbo.ld3methodparameter ("OrdNum","MethodID","Name","InternalName","Deleted","DataType","IsResult","IsRequired","Direction","DefaultValue")
    select 1,tmpProcID,'oldFileID','oldfileid','-',1,'-','-','I',null
    union all
    select 2,tmpProcID,'newFileID','newfileid','-',1,'-','-','I',null
    union all
    select 2,tmpProcID,'pResult','presult','-',1,'+','-','O',null;

    insert into DBO.LDUsedScripts (name,description,userlogin,ExecDate)
    select tmpScriptName,tmpScriptDescr,current_user,current_timestamp;
exception
    when others then
    get stacked diagnostics tmpErrorMsg = message_text,
                            tmpErrorText = pg_exception_detail,
                            tmpErrorContext = pg_exception_context;
    tmpErrorMsg := 'При выполнении задачи "'||tmpScriptName||'" произошла ошибка: '||tmpErrorMsg;
    insert into DBO.LDUsedScripts (name,description,userlogin,execdate)
    select tmpScriptName,tmpScriptDescr,current_user,current_timestamp;
    raise exception '%детально: %контекст: %',tmpErrorMsg||','||chr(10),tmpErrorText||','||chr(10),tmpErrorContext;
end$$;



----------------------------------------------------------------------------------------------
do $$
declare
  tmpScriptName text;
  tmpScriptDescr text;
begin
  tmpScriptName :=  'Psgsql_001_ver3.9.1.x_02.05.2025   Сервис формирования Файла Визуализации.sql';
  tmpScriptDescr := 'Автор: Валявский А.А. Дата:02.05.2025   Сервис формирования Файла Визуализации';
  insert into DBO.LDUsedScripts (name,description,userlogin,execdate) select tmpScriptName,tmpScriptDescr,current_user,current_timestamp;
END$$;












